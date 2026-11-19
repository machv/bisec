using BiSec.Library.Exceptions;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BiSec.Library
{
    public class GatewayConnection : IDisposable
    {
        public const int DefaultPort = 4000;

        const string DefaultToken = "00000000";
        const string DefaultSenderId = "000000000000";
        const int BroadcastTag = 0xFF;
        const int TagCount = 128;
        const int ConnectRetryDelay = 250;

        private sealed class Session
        {
            public TcpClient Client;
            public NetworkStream Stream;
            public CancellationTokenSource Cancellation;
            public int Closed;
        }

        private sealed class PendingRequest
        {
            public Message Request;
            public Session Session;
            public TaskCompletionSource<Package> Completion;
        }

        private ILogger _logger;

        private readonly IPAddress _address;
        private readonly int _port;
        private readonly string _receiverId;
        private volatile string _senderId;
        private volatile string _token;
        private volatile Session _session;

        // The gateway handles one MCP request at a time, so sends are serialized.
        private readonly SemaphoreSlim _sendGate = new SemaphoreSlim(1, 1);
        private readonly ConcurrentDictionary<int, PendingRequest> _unconfirmedPackages = new ConcurrentDictionary<int, PendingRequest>();
        private readonly object _tagAllocationLock = new object();
        private int _nextTag;

        public IPAddress Address => _address;
        public int Port => _port;
        public string GatewayId => _receiverId;
        public string ClientId => _senderId;
        public string Token => _token;
        public bool IsAuthenticated => _token != DefaultToken;

        public bool Connected
        {
            get
            {
                Session session = _session;
                return session != null && session.Closed == 0 && session.Client.Connected;
            }
        }

        public RequestTimeouts Timeouts { get; } = new RequestTimeouts();

        /// <summary>
        /// The total time allowed for connecting. A real gateway was at times slow to accept a
        /// connection: several seconds, once more than ten, with no pattern found. Within this time
        /// connecting is attempted repeatedly: an attempt that fails because the host is unreachable,
        /// or that gets no answer within <see cref="ConnectAttemptTimeout"/>, is replaced by a new one.
        /// </summary>
        public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(20);

        /// <summary>
        /// How long a single connect attempt may stay unanswered; the legacy app's TCP connect timeout.
        /// A new attempt reaches a gateway that has just become reachable sooner than the operating
        /// system's own, ever slower, retransmissions would.
        /// </summary>
        public TimeSpan ConnectAttemptTimeout { get; set; } = TimeSpan.FromMilliseconds(1500);

        // Replaced in tests to stand in for a gateway that is unreachable at first.
        internal Func<TcpClient, IPAddress, int, CancellationToken, Task> ConnectSocketAsync { get; set; }
            = (client, address, port, cancellationToken) => client.ConnectAsync(address, port, cancellationToken).AsTask();

        /// <summary>
        /// How long the rest of a package may take to arrive once its first bytes are in. A package
        /// that stalls longer cannot be recovered, so the connection is closed. The legacy app
        /// gives up on a partial package after the same 500 ms.
        /// </summary>
        public TimeSpan PackageCompletionTimeout { get; set; } = TimeSpan.FromMilliseconds(500);

        /// <summary>
        /// Raised for every package received with a valid token, on the receive loop.
        /// Handlers must not block.
        /// </summary>
        public event EventHandler<PackageReceivedEventArgs> PackageReceived;

        /// <summary>
        /// Raised when the gateway pushes a LOGOUT (tag 0xFF), ending the session.
        /// </summary>
        public event EventHandler LoggedOut;

        public event EventHandler Disconnected;

        public GatewayConnection(DiscoveryResult disoveryResult) : this(disoveryResult.SourceAddress, DefaultSenderId, disoveryResult.GatewayId)
        {
        }

        public GatewayConnection(IPAddress address, string senderId, string receiverId, int port = DefaultPort)
        {
            _address = address ?? throw new ArgumentNullException(nameof(address));
            _senderId = senderId ?? DefaultSenderId;
            _receiverId = receiverId ?? throw new ArgumentNullException(nameof(receiverId));
            _port = port;
            _token = DefaultToken;
        }

        public void SetLogger(ILogger logger)
        {
            _logger = logger;
        }

        public void Connect()
        {
            ConnectAsync().GetAwaiter().GetResult();
        }

        public async Task ConnectAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            if (Connected)
                return;

            TcpClient client = null;
            string lastFailure = null;

            using (CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                timeout.CancelAfter(ConnectTimeout);
                try
                {
                    while (true)
                    {
                        client = new TcpClient();
                        using (CancellationTokenSource attempt = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token))
                        {
                            attempt.CancelAfter(ConnectAttemptTimeout);
                            try
                            {
                                await ConnectSocketAsync(client, _address, _port, attempt.Token).ConfigureAwait(false);
                                break;
                            }
                            catch (SocketException e) when (e.SocketErrorCode == SocketError.HostUnreachable || e.SocketErrorCode == SocketError.NetworkUnreachable)
                            {
                                lastFailure = e.Message;
                                client.Dispose();
                                _logger?.LogDebug($"Gateway {_address} is not reachable yet ({e.Message}); trying again.");
                                await Task.Delay(ConnectRetryDelay, timeout.Token).ConfigureAwait(false);
                            }
                            catch (OperationCanceledException) when (!timeout.IsCancellationRequested)
                            {
                                lastFailure = $"no answer within {ConnectAttemptTimeout.TotalMilliseconds:0} ms";
                                client.Dispose();
                                _logger?.LogDebug($"Gateway {_address} did not answer the connect attempt; trying again.");
                            }
                        }
                    }
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    client?.Dispose();
                    string detail = lastFailure == null ? "" : $" Last error: {lastFailure}.";
                    throw new TimeoutException($"Connecting to gateway {_address}:{_port} timed out after {ConnectTimeout.TotalMilliseconds:0} ms.{detail}");
                }
                catch
                {
                    client?.Dispose();
                    throw;
                }
            }

            Session session = new Session
            {
                Client = client,
                Stream = client.GetStream(),
                Cancellation = new CancellationTokenSource(),
            };

            _token = DefaultToken;
            _session = session;

            _ = Task.Run(() => ReceiveLoopAsync(session));
        }

        public void Disconnect()
        {
            Close(_session, new IOException("The gateway connection was closed locally."));
        }

        public void Dispose()
        {
            Disconnect();
        }

        /// <summary>
        /// Forgets the session token without waiting for the gateway to confirm a logout.
        /// </summary>
        internal void ClearToken()
        {
            _token = DefaultToken;
        }

        /// <summary>
        /// Sends a request and completes with the matching response. Sets the tag and token on
        /// <paramref name="message"/>. Throws <see cref="TimeoutException"/> when the gateway does not
        /// answer in time and <see cref="IOException"/> when the connection drops while waiting.
        /// </summary>
        public async Task<Package> SendAsync(Message message, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (message == null)
                throw new ArgumentNullException(nameof(message));

            await _sendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                Session session = _session;
                if (session == null || session.Closed != 0)
                    throw new InvalidOperationException("The gateway connection is not open. Call ConnectAsync first.");

                if (message.Command == Command.Login)
                {
                    // if there is new login, reset the previous token authorization
                    _token = DefaultToken;
                }

                if (_token == DefaultToken && message.Command.AuthenticationRequired)
                    throw new InvalidOperationException("Message requires authentication, but the gateway connection has no active token.");

                PendingRequest pending = new PendingRequest
                {
                    Request = message,
                    Session = session,
                    Completion = new TaskCompletionSource<Package>(TaskCreationOptions.RunContinuationsAsynchronously),
                };

                int tag = AllocateTag(pending);
                try
                {
                    message.Tag = (byte)tag;
                    message.Token = _token;
                    Package package = new Package(_senderId, _receiverId, message);

                    await WritePackageAsync(session, package, cancellationToken).ConfigureAwait(false);

                    int timeout = Timeouts.Get(message.Command);
                    try
                    {
                        return await pending.Completion.Task.WaitAsync(TimeSpan.FromMilliseconds(timeout), cancellationToken).ConfigureAwait(false);
                    }
                    catch (TimeoutException)
                    {
                        throw new TimeoutException($"Request {message.Command.Name} (Tag: {tag}) timed out after {timeout} ms.");
                    }
                }
                finally
                {
                    _unconfirmedPackages.TryRemove(tag, out _);
                }
            }
            finally
            {
                _sendGate.Release();
            }
        }

        private async Task WritePackageAsync(Session session, Package package, CancellationToken cancellationToken)
        {
            string hexString = StringHelper.HexStringFromByteArray(package.ToByteArray());
            byte[] toSend = Encoding.ASCII.GetBytes(hexString);

            _logger?.LogTrace($"Sending package: {hexString}");

            try
            {
                await session.Stream.WriteAsync(toSend, 0, toSend.Length, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e) when (e is IOException || e is ObjectDisposedException || e is SocketException)
            {
                IOException failure = new IOException("Sending to the gateway failed; the connection was closed.", e);
                Close(session, failure);
                throw failure;
            }
        }

        private async Task ReceiveLoopAsync(Session session)
        {
            CancellationToken cancellationToken = session.Cancellation.Token;
            Exception failure = null;

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    byte[] frame = await ReadFrameAsync(session.Stream, PackageCompletionTimeout, cancellationToken).ConfigureAwait(false);

                    Package package;
                    try
                    {
                        if (!IsHex(frame, 0, frame.Length))
                            throw new FormatException("The frame contains characters that are not hexadecimal digits.");

                        _logger?.LogTrace($"Received package: {Encoding.ASCII.GetString(frame)}");
                        package = Package.Load(frame);
                    }
                    catch (Exception e) when (!(e is OperationCanceledException))
                    {
                        // The frame boundaries were intact, so only this package is lost.
                        _logger?.LogWarning($"Ignoring malformed package from gateway: {e.Message}");
                        continue;
                    }

                    HandlePackage(package);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (EndOfStreamException e)
            {
                failure = new IOException("The gateway closed the connection.", e);
            }
            catch (InvalidPackageLengthException e)
            {
                // Without a trustworthy length there is no way to find the next frame.
                failure = new IOException("The gateway stream is out of sync; the connection was closed.", e);
                _logger?.LogError($"Receive stream out of sync ({e.Message}) -> closing the session.");
            }
            catch (Exception e)
            {
                failure = new IOException("Receiving from the gateway failed; the connection was closed.", e);
                _logger?.LogError($"Exception occured while reading package ({e.Message}) -> closing the session.");
            }
            finally
            {
                Close(session, failure ?? new IOException("The gateway connection was closed."));
            }
        }

        private static async Task<byte[]> ReadFrameAsync(Stream stream, TimeSpan completionTimeout, CancellationToken cancellationToken)
        {
            int headerSize = Lengths.ADDRESS_SIZE + Lengths.ADDRESS_SIZE + Lengths.LENGTH_SIZE;
            byte[] header = new byte[headerSize];

            // Waiting for a package to begin has no limit; only its completion does.
            int started = await stream.ReadAsync(header, 0, headerSize, cancellationToken).ConfigureAwait(false);
            if (started == 0)
                throw new EndOfStreamException();

            await ReadRestAsync(stream, header, started, headerSize - started, completionTimeout, cancellationToken).ConfigureAwait(false);

            if (!IsHex(header, 0, headerSize))
                throw new InvalidPackageLengthException("The package header is not hexadecimal text.");

            int bodyLength = 0;
            for (int i = headerSize - Lengths.LENGTH_SIZE; i < headerSize; i++)
                bodyLength = (bodyLength << 4) | StringHelper.GetHexValue(header[i]);

            if (bodyLength < Lengths.MinimalMessageBytes)
                throw new InvalidPackageLengthException($"The package announces a body of {bodyLength} bytes, less than the minimum of {Lengths.MinimalMessageBytes}.");

            // body length + checksum (1 byte) - length (2 bytes) * 2 (encoding)
            int remaining = (bodyLength + Lengths.CHECKSUM_BYTES - Lengths.LENGTH_BYTES) * Lengths.BYTE_LENGTH;
            byte[] frame = new byte[headerSize + remaining];
            Array.Copy(header, frame, headerSize);
            await ReadRestAsync(stream, frame, headerSize, remaining, completionTimeout, cancellationToken).ConfigureAwait(false);

            return frame;
        }

        // Reads the remainder of a package that has started; each chunk must arrive within the timeout.
        private static async Task ReadRestAsync(Stream stream, byte[] buffer, int offset, int count, TimeSpan completionTimeout, CancellationToken cancellationToken)
        {
            int total = 0;
            while (total < count)
            {
                int read;
                using (CancellationTokenSource stall = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    stall.CancelAfter(completionTimeout);
                    try
                    {
                        read = await stream.ReadAsync(buffer, offset + total, count - total, stall.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        throw new InvalidPackageLengthException($"A package stopped arriving after {offset + total} characters.");
                    }
                }

                if (read == 0)
                    throw new EndOfStreamException();

                total += read;
            }
        }

        private void HandlePackage(Package package)
        {
            Message message = package.Message;

            _logger?.LogDebug($"Decoded package: Command = {message.Command}, Tag = {message.Tag}");

            // Same order as the legacy app's MCPPackageQueue.packageReceive_handler:
            // the token is judged first, then LOGIN/LOGOUT may replace it.
            bool validToken = IsValidToken(message.Token);

            if (message.Command == Command.Login && message.IsResponse)
            {
                _token = ReadLoginToken(message.Payload.ToByteArray());
            }
            else if (message.Command == Command.Logout)
            {
                _token = DefaultToken;
                if (message.Tag == BroadcastTag)
                    Raise(LoggedOut);
            }

            if (!validToken)
            {
                _logger?.LogInformation($"Received package with invalid token for command {message.Command}; ignoring.");
                return;
            }

            // WIFI_FOUND is a notification by definition; it must never complete a request.
            PendingRequest pending = null;
            bool unsolicited = message.Tag == BroadcastTag
                || message.Command == Command.WifiFound
                || !_unconfirmedPackages.TryGetValue(message.Tag, out pending);

            if (unsolicited)
            {
                _logger?.LogDebug($"Received unsolicited package {message.Command} (Tag: {message.Tag}).");
            }
            else
            {
                // The gateway addresses responses to the client id it assigned to this connection.
                _senderId = package.Receiver;
            }

            EventHandler<PackageReceivedEventArgs> handler = PackageReceived;
            if (handler != null)
            {
                try
                {
                    handler(this, new PackageReceivedEventArgs(package, unsolicited));
                }
                catch (Exception e)
                {
                    _logger?.LogError($"A PackageReceived handler failed: {e.Message}");
                }
            }

            // Completed after the event, so whoever awaits the response knows its event has been delivered.
            if (!unsolicited)
                pending.Completion.TrySetResult(package);
        }

        private void Close(Session session, Exception reason)
        {
            if (session == null || Interlocked.Exchange(ref session.Closed, 1) != 0)
                return;

            try
            {
                session.Cancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }

            session.Client.Dispose();

            // A newer session may already be in place; its token and requests are not this one's to touch.
            if (ReferenceEquals(_session, session))
                _token = DefaultToken;

            foreach (PendingRequest pending in _unconfirmedPackages.Values)
            {
                if (ReferenceEquals(pending.Session, session))
                    pending.Completion.TrySetException(reason);
            }

            Raise(Disconnected);
        }

        private void Raise(EventHandler handler)
        {
            if (handler == null)
                return;

            try
            {
                handler(this, EventArgs.Empty);
            }
            catch (Exception e)
            {
                _logger?.LogError($"A connection event handler failed: {e.Message}");
            }
        }

        // Tags rotate instead of reusing the lowest free one, so that a late answer to a request
        // that timed out cannot be taken for the answer to the next request.
        private int AllocateTag(PendingRequest request)
        {
            lock (_tagAllocationLock)
            {
                for (int i = 0; i < TagCount; i++)
                {
                    int tag = (_nextTag + i) % TagCount;
                    if (_unconfirmedPackages.TryAdd(tag, request))
                    {
                        _nextTag = (tag + 1) % TagCount;
                        return tag;
                    }
                }
            }

            throw new InvalidOperationException("No free MCP package tag is available.");
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

            // LOGIN response payload: [userId][token:4]
            int offset = payload.Length >= 5 ? 1 : 0;
            byte[] tokenBytes = new byte[4];
            Array.Copy(payload, offset, tokenBytes, 0, tokenBytes.Length);

            return StringHelper.HexStringFromByteArray(tokenBytes);
        }

        private static bool IsHex(byte[] buffer, int offset, int count)
        {
            for (int i = offset; i < offset + count; i++)
            {
                byte b = buffer[i];
                bool hex = (b >= '0' && b <= '9') || (b >= 'A' && b <= 'F') || (b >= 'a' && b <= 'f');
                if (!hex)
                    return false;
            }

            return true;
        }
    }
}
