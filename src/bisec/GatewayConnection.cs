using BiSec.Library.Exceptions;
using Microsoft.Extensions.Logging;
using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
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

        const int DefaultPort = 4000;
        const int DefaultSendTimeout = 2000;
        const int DefaultReadTimeout = 2000;

        private IPAddress _address;
        private string _senderId;
        private string _receiverId;
        private string _token;
        private int _port;

        protected TcpClient _client;
        protected NetworkStream _ns;
        protected CancellationTokenSource _consumeSendingQueueCancellation;
        protected readonly BlockingCollection<Package> _sendingQueue;
        protected readonly BlockingCollection<Package> _receivingQueue;
        protected readonly ConcurrentDictionary<int, PackageQueueItem> _uncofirmedPackages;

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

            _sendingQueue = new BlockingCollection<Package>();
            _receivingQueue = new BlockingCollection<Package>();
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

            ThreadPool.QueueUserWorkItem(new WaitCallback(ProcessOutgoing));
            ThreadPool.QueueUserWorkItem(new WaitCallback(ProcessIncoming));
        }

        private void ProcessOutgoing(object state)
        {
            _consumeSendingQueueCancellation = new CancellationTokenSource();

            while (_client.Connected)
            {
                try
                {
                    foreach (Package message in _sendingQueue.GetConsumingEnumerable(_consumeSendingQueueCancellation.Token))
                    {
                        if (message != null)
                        {
                            if (message.Message.Command == Command.Login)
                            {
                                // if there is new login, reset the previous token authorization
                                _token = DefaultToken;
                            }

                            if (_token == DefaultToken && message.Message.Command.AuthenticationRequired)
                            {
                                // Message to be sent, but not authenticated! Ignoring message...
                                continue;
                            }

                            // set the authorization
                            message.Message.Token = _token;
                            var package = new Package(_senderId, message.Receiver, message.Message);

                            WriteMessage(_ns, package);
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

        private async void ProcessIncoming(object state)
        {
            while (_client.Connected)
            {
                byte[] packageBytes = null;

                packageBytes = await ReadPackage().ConfigureAwait(false);

                if (packageBytes == null)
                {
                    // connection was terminated -> end processing of incoming messages.
                    return;
                }
                Package package = Package.Load(packageBytes);

                _logger?.LogDebug($"Decoded package: Command = {package.Message.Command}, Tag = {package.Message.Tag}");

                if (package.Message.Command == Command.Login)
                {
                    _senderId = package.Receiver;
                    _token = StringHelper.HexStringFromByteArray(package.Message.Payload.ToByteArray()).Substring(2);
                }

                try
                {
                    // Check if the recieved message is confirming previous one
                    PackageQueueItem unconfirmed = null;
                    _uncofirmedPackages.TryGetValue(package.Message.Tag, out unconfirmed);
                    if (unconfirmed != null)
                    {
                        unconfirmed.SetResult(package);

                        PackageQueueItem removed;
                        _uncofirmedPackages.TryRemove(package.Message.Tag, out removed);
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine(e.Message);
                }
            }
        }

        protected async Task<byte[]> ReadPackage()
        {
            int bytesCount = Lengths.ADDRESS_SIZE + Lengths.ADDRESS_SIZE + Lengths.LENGTH_SIZE;
            byte[] headerBuffer = new byte[bytesCount];
            
            int length;
            try
            {
                length = await _ns.ReadAsync(headerBuffer, 0, bytesCount).ConfigureAwait(false);
            }
            catch (IOException e)
            {
                _logger?.LogError($"Exception occured while reading package header ({e.Message}) -> closing the session.");

                TerminateSession();

                return null;
            }

            if (length < bytesCount)
                throw new InvalidPackageLengthException("Received bytes are too short for a complete package.");

            var rawPackageHeader = ConversionHelper.GatewayPayloadToByteArray(headerBuffer);

            short bodyLength = BinaryPrimitives.ReadInt16BigEndian(rawPackageHeader[12..14]); // body length

            int remainingDataLength = (bodyLength + Lengths.CHECKSUM_BYTES - Lengths.LENGTH_BYTES) * Lengths.BYTE_LENGTH; // body length + checksum (1 byte) - length (2 bytes) * 2 (encoding)
            byte[] messageBuffer = new byte[remainingDataLength];
            length = await _ns.ReadAsync(messageBuffer, 0, remainingDataLength).ConfigureAwait(false);

            if (length != remainingDataLength)
                throw new Exception("Read bytes length differs from expected length.");

            int messageLength = headerBuffer.Length + messageBuffer.Length;
            byte[] message = new byte[messageLength];

            Array.Copy(headerBuffer, message, headerBuffer.Length);
            Array.Copy(messageBuffer, 0, message, headerBuffer.Length, messageBuffer.Length);

            string receivedMessage = Encoding.UTF8.GetString(message);
            _logger?.LogTrace($"Received package: {receivedMessage}");

            return message;
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
            catch (IOException e)
            {
                //_logger?.Error(string.Format("Exception occured while writing PDU ({0}) -> closing session.", e.Message));

                TerminateSession();
            }
        }

        protected void TerminateSession()
        {
            //if (_state == ConnectionState.Closed) // if already disposed, we can ignore it
            //    return;

            Dispose();

            // clear working data
            _sendingQueue.CompleteAdding();
            _consumeSendingQueueCancellation.Cancel();
        }

        public void Dispose()
        {
            //if (_state == ConnectionState.Closed)
            //    return;

            //_state = ConnectionState.Closed;

            _client.Client.Shutdown(SocketShutdown.Send);
        }


        public void Send(Message message)
        {
            if (_token == DefaultToken && message.Command.AuthenticationRequired)
            {
                Console.WriteLine("Message to be sent, but not authenticated! Ignoring message...");
                return;
            }

            Package package = new Package(_senderId, _receiverId, message);
            QueuePdu(package);
        }

        protected void QueuePdu(Package message)
        {
            TaskCompletionSource<Package> tcs = new TaskCompletionSource<Package>();
            PackageQueueItem<Package> item = new PackageQueueItem<Package>(message, tcs);

            QueuePackage(item);
        }

        protected void QueuePackage(PackageQueueItem queueItem)
        {
            if (queueItem == null)
                throw new ArgumentNullException("queueItem");

            if (queueItem.Package == null)
                throw new ArgumentNullException("queueItem.Package");

            // Add message for confirmation
            _uncofirmedPackages.TryAdd(queueItem.Package.Message.Tag, queueItem);

            // And then send it
            _sendingQueue.Add(queueItem.Package);
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
                if (cancellationToken != null)
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
    }
}
