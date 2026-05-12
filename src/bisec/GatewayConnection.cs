using BiSec.Library.Exceptions;
using Microsoft.Extensions.Logging;
using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BiSec.Library
{
    public class GatewayConnection
    {
        private ILogger _logger;

        const string DefaultToken = "00000000";
        const string DefaultSenderId = "000000000000";
        const int BroadcastTag = 0xFF;

        const int DefaultPort = 4000;
        const int DefaultRequestTimeout = 5000;
        const int LoginRequestTimeout = 10000;
        const int JmcpRequestTimeout = 30000;
        const int StateRequestTimeout = 20000;

        private IPAddress _address;
        private string _senderId;
        private string _receiverId;
        private string _token;
        private int _port;

        protected TcpClient _client;
        protected NetworkStream _ns;
        protected CancellationTokenSource _consumeSendingQueueCancellation;
        protected readonly BlockingCollection<PackageQueueItem> _sendingQueue;
        protected readonly ConcurrentDictionary<int, PackageQueueItem> _uncofirmedPackages;
        private readonly object _tagAllocationLock = new object();

        public bool Connected => _client?.Connected ?? false;

        public string Token
        {
            get => _token;
        }

        public GatewayConnection(DiscoveryResult disoveryResult) : this(disoveryResult.SourceAddress, "000000000000", disoveryResult.GatewayId)
        {

        }

        public GatewayConnection(IPAddress address, string senderId, string receiverId)
        {
            _address = address;
            _senderId = senderId;
            _receiverId = receiverId;
            _token = DefaultToken;
            _port = DefaultPort;

            _sendingQueue = new BlockingCollection<PackageQueueItem>();
            _uncofirmedPackages = new ConcurrentDictionary<int, PackageQueueItem>();
        }

        public void SetLogger(ILogger logger)
        {
            _logger = logger;
        }

        public void Connect()
        {
            _client = new TcpClient(_address.ToString(), _port);
            _ns = _client.GetStream();
            _consumeSendingQueueCancellation = new CancellationTokenSource();

            _ = ProcessOutgoingAsync(_consumeSendingQueueCancellation.Token);
            _ = ProcessIncomingAsync(_consumeSendingQueueCancellation.Token);
        }

        public void Disconnect()
        {
            TerminateSession();
        }

        private async Task ProcessOutgoingAsync(CancellationToken cancellationToken)
        {
            while (_client.Connected && !cancellationToken.IsCancellationRequested)
            {
                try
                {
                    foreach (PackageQueueItem item in _sendingQueue.GetConsumingEnumerable(cancellationToken))
                    {
                        Package package = item?.Package;
                        if (package != null)
                        {
                            if (package.Message.Command == Command.Login)
                            {
                                // if there is new login, reset the previous token authorization
                                _token = DefaultToken;
                            }

                            if (_token == DefaultToken && package.Message.Command.AuthenticationRequired)
                            {
                                item.SetException(new InvalidOperationException("Message requires authentication, but the gateway connection has no active token."));
                                RemoveUnconfirmedPackage(package.Message.Tag);
                                continue;
                            }

                            // set the authorization
                            package.Message.Token = _token;
                            package = new Package(_senderId, package.Receiver, package.Message);

                            WriteMessage(_ns, package);
                            await WaitForResponseOrTimeoutAsync(item, cancellationToken).ConfigureAwait(false);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    // reading cancelled -> quit this thread
                    break;
                }
            }
        }

        private async Task ProcessIncomingAsync(CancellationToken cancellationToken)
        {
            while (_client.Connected && !cancellationToken.IsCancellationRequested)
            {
                byte[] packageBytes = null;

                packageBytes = await ReadPackage(cancellationToken).ConfigureAwait(false);

                if (packageBytes == null)
                {
                    // connection was terminated -> end processing of incoming messages.
                    return;
                }
                Package package = Package.Load(packageBytes);

                _logger?.LogDebug($"Decoded package: Command = {package.Message.Command}, Tag = {package.Message.Tag}");

                if (!IsValidToken(package.Message.Token))
                {
                    _logger?.LogInformation($"Received package with invalid token for command {package.Message.Command}; ignoring.");
                    continue;
                }

                if (package.Message.Command == Command.Login && package.Message.IsResponse)
                {
                    _senderId = package.Receiver;
                    _token = ReadLoginToken(package.Message.Payload.ToByteArray());
                }
                else if (package.Message.Command == Command.Logout && package.Message.IsResponse)
                {
                    // APK MCPPackageQueue.packageReceive_handler resets the connection
                    // token to 0 whenever a LOGOUT response is received.
                    _token = DefaultToken;
                }

                try
                {
                    if (package.Message.Tag == BroadcastTag)
                    {
                        _logger?.LogDebug("Received broadcast package with tag 0xFF; no request callback will be completed.");
                        continue;
                    }

                    // Check if the received message is confirming previous one
                    PackageQueueItem unconfirmed = null;
                    _uncofirmedPackages.TryGetValue(package.Message.Tag, out unconfirmed);
                    if (unconfirmed != null)
                    {
                        unconfirmed.SetResult(package);

                        RemoveUnconfirmedPackage(package.Message.Tag);
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine(e.Message);
                }
            }
        }

        protected async Task<byte[]> ReadPackage(CancellationToken cancellationToken = default(CancellationToken))
        {
            int bytesCount = Lengths.ADDRESS_SIZE + Lengths.ADDRESS_SIZE + Lengths.LENGTH_SIZE;
            byte[] headerBuffer = new byte[bytesCount];

            try
            {
                await ReadExactAsync(_ns, headerBuffer, 0, bytesCount, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e) when (e is IOException || e is EndOfStreamException || e is ObjectDisposedException)
            {
                _logger?.LogError($"Exception occured while reading package header ({e.Message}) -> closing the session.");

                TerminateSession();

                return null;
            }

            var rawPackageHeader = ConversionHelper.GatewayPayloadToByteArray(headerBuffer);

            short bodyLength = BinaryPrimitives.ReadInt16BigEndian(rawPackageHeader[12..14]); // body length

            int remainingDataLength = (bodyLength + Lengths.CHECKSUM_BYTES - Lengths.LENGTH_BYTES) * Lengths.BYTE_LENGTH; // body length + checksum (1 byte) - length (2 bytes) * 2 (encoding)
            byte[] messageBuffer = new byte[remainingDataLength];
            try
            {
                await ReadExactAsync(_ns, messageBuffer, 0, remainingDataLength, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e) when (e is IOException || e is EndOfStreamException || e is ObjectDisposedException)
            {
                _logger?.LogError($"Exception occured while reading package body ({e.Message}) -> closing the session.");

                TerminateSession();

                return null;
            }

            int messageLength = headerBuffer.Length + messageBuffer.Length;
            byte[] message = new byte[messageLength];

            Array.Copy(headerBuffer, message, headerBuffer.Length);
            Array.Copy(messageBuffer, 0, message, headerBuffer.Length, messageBuffer.Length);

            string receivedMessage = Encoding.UTF8.GetString(message);
            _logger?.LogTrace($"Received package: {receivedMessage}");

            return message;
        }

        private static async Task ReadExactAsync(Stream stream, byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            int totalRead = 0;
            while (totalRead < count)
            {
                int read = await stream.ReadAsync(buffer, offset + totalRead, count - totalRead, cancellationToken).ConfigureAwait(false);
                if (read == 0)
                    throw new EndOfStreamException("The gateway closed the connection before the package was fully read.");

                totalRead += read;
            }
        }

        protected void WriteMessage(NetworkStream stream, Package tc)
        {
            if (stream == null)
                throw new ArgumentNullException();

            if (tc == null)
                throw new ArgumentNullException();

            byte[] data = tc.ToByteArray();

            string hexString = StringHelper.HexStringFromByteArray(data);
            byte[] toSend = hexString.ToByteArray();

            try
            {
                stream.Write(toSend, 0, toSend.Length);
            }
            catch (IOException)
            {
                TerminateSession();
            }
        }

        protected void TerminateSession()
        {
            //if (_state == ConnectionState.Closed) // if already disposed, we can ignore it
            //    return;

            Dispose();

            // clear working data
            if (!_sendingQueue.IsAddingCompleted)
                _sendingQueue.CompleteAdding();

            _consumeSendingQueueCancellation?.Cancel();
        }

        public void Dispose()
        {
            //if (_state == ConnectionState.Closed)
            //    return;

            //_state = ConnectionState.Closed;

            try
            {
                _client?.Client?.Shutdown(SocketShutdown.Send);
            }
            catch (SocketException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        }


        public void Send(Message message)
        {
            _ = SendAsync(message);
        }

        public Task<Package> SendAsync(Message message, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (message == null)
                throw new ArgumentNullException(nameof(message));

            Package package = new Package(_senderId, _receiverId, message);
            return QueuePdu(package, cancellationToken);
        }

        protected Task<Package> QueuePdu(Package package, CancellationToken cancellationToken)
        {
            TaskCompletionSource<Package> tcs = new TaskCompletionSource<Package>(TaskCreationOptions.RunContinuationsAsynchronously);
            PackageQueueItem<Package> item = new PackageQueueItem<Package>(package, tcs);

            QueuePackage(item, cancellationToken);

            return tcs.Task;
        }

        protected void QueuePackage(PackageQueueItem queueItem, CancellationToken cancellationToken)
        {
            if (queueItem == null)
                throw new ArgumentNullException("queueItem");

            if (queueItem.Package == null)
                throw new ArgumentNullException("queueItem.Package");

            int tag = AllocateTag(queueItem);
            queueItem.Package.Message.Tag = (byte)tag;

            if (cancellationToken.CanBeCanceled)
            {
                cancellationToken.Register(() =>
                {
                    queueItem.SetException(new OperationCanceledException(cancellationToken));
                    RemoveUnconfirmedPackage(queueItem.Package.Message.Tag);
                });
            }

            _sendingQueue.Add(queueItem, cancellationToken);
        }

        public Message Read(int tag)
        {
            _uncofirmedPackages.TryGetValue(tag, out PackageQueueItem queueItem);

            throw new NotImplementedException();
        }

        public Task<Package> GetResponseAsync(int tag, CancellationToken cancellationToken = default(CancellationToken))
        {
            return GetResponseAsync<Package>(tag, cancellationToken);
        }

        public Task<T> GetResponseAsync<T>(int tag, CancellationToken cancellationToken = default(CancellationToken)) where T : Package
        {
            _uncofirmedPackages.TryGetValue(tag, out PackageQueueItem queueItem);

            if (queueItem is PackageQueueItem<T> item)
            {
                if (cancellationToken.CanBeCanceled)
                {
                    cancellationToken.Register(() =>
                    {
                        item.TaskCompletionSource.TrySetCanceled(cancellationToken);
                    });
                }

                return item.TaskCompletionSource.Task;
            }

            throw new InvalidOperationException("Cannot find message in the queue.");
        }

        private int AllocateTag(PackageQueueItem queueItem)
        {
            lock (_tagAllocationLock)
            {
                for (int tag = 0; tag < 128; tag++)
                {
                    if (_uncofirmedPackages.TryAdd(tag, queueItem))
                        return tag;
                }
            }

            throw new InvalidOperationException("No free MCP package tag is available.");
        }

        private void RemoveUnconfirmedPackage(int tag)
        {
            PackageQueueItem removed;
            _uncofirmedPackages.TryRemove(tag, out removed);
        }

        private async Task WaitForResponseOrTimeoutAsync(PackageQueueItem item, CancellationToken cancellationToken)
        {
            int timeout = GetRequestTimeout(item.Package.Message.Command);
            Task timeoutTask = Task.Delay(timeout, cancellationToken);
            Task responseTask = item.Completion;

            Task completed = await Task.WhenAny(responseTask, timeoutTask).ConfigureAwait(false);
            if (completed == timeoutTask && !responseTask.IsCompleted)
            {
                item.SetException(new TimeoutException($"Request {item.Package.Message.Command} (Tag: {item.Package.Message.Tag}) timed out after {timeout} ms."));
                RemoveUnconfirmedPackage(item.Package.Message.Tag);
            }
        }

        private static int GetRequestTimeout(Command command)
        {
            if (command == Command.Login)
                return LoginRequestTimeout;

            if (command == Command.Jmcp)
                return JmcpRequestTimeout;

            if (command == Command.SetState || command == Command.GetTransition)
                return StateRequestTimeout;

            return DefaultRequestTimeout;
        }

        private bool IsValidToken(string token)
        {
            return string.Equals(NormalizeToken(token), DefaultToken, StringComparison.OrdinalIgnoreCase)
                || string.Equals(NormalizeToken(token), NormalizeToken(_token), StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeToken(string token)
        {
            return string.IsNullOrWhiteSpace(token)
                ? DefaultToken
                : token.PadLeft(DefaultToken.Length, '0').ToUpperInvariant();
        }

        private static string ReadLoginToken(byte[] payload)
        {
            if (payload == null || payload.Length < 4)
                return DefaultToken;

            int offset = payload.Length >= 5 ? 1 : 0;
            byte[] tokenBytes = new byte[4];
            Array.Copy(payload, offset, tokenBytes, 0, tokenBytes.Length);

            return StringHelper.HexStringFromByteArray(tokenBytes);
        }
    }
}
