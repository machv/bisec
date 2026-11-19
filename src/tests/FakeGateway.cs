using BiSec.Library;
using BiSec.Library.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace tests
{
    public sealed class FakeUser
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Password { get; set; }
        public List<int> Groups { get; set; } = new List<int>();
    }

    public sealed class FakeGroup
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public List<int> Ports { get; set; } = new List<int>();
    }

    /// <summary>
    /// A request as the fake gateway received it.
    /// </summary>
    public sealed class FakeRequest
    {
        public Command Command { get; set; }
        public byte[] Payload { get; set; }
        public string Sender { get; set; }
        public string Receiver { get; set; }
        public byte Tag { get; set; }
        public string Token { get; set; }
        public bool IsResponse { get; set; }
        public bool ChecksumsValid { get; set; }

        public string Text => Encoding.UTF8.GetString(Payload);
        public string Hex => StringHelper.HexStringFromByteArray(Payload);

        public override string ToString()
        {
            return $"{Command.Name} [{Hex}]";
        }
    }

    public enum SetStateReply
    {
        /// <summary>SET_STATE is answered with an HM_GET_TRANSITION response, as the legacy app expects.</summary>
        Transition,

        /// <summary>SET_STATE is answered with a plain SET_STATE response without payload.</summary>
        Acknowledge,
    }

    /// <summary>
    /// An in-process BiSecur gateway on a loopback TCP port. It speaks the hex-text framing through the
    /// library's own <see cref="Package"/> and <see cref="Message"/> classes and keeps its state in memory.
    /// Where the real gateway's behaviour is not known from the legacy app (payload of plain
    /// acknowledgements, the response bit and token of pushed packages, sessions being bound to the
    /// connection) the fake makes the simplest assumption; those spots are marked.
    /// </summary>
    public sealed class FakeGateway : IDisposable
    {
        public const string GatewayId = "5410EC852A31";
        public const string AssignedClientId = "000000000006";
        public const byte BroadcastTag = 0xFF;
        public const string NoToken = "00000000";

        private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(10);

        private sealed class Connection
        {
            public TcpClient Client;
            public NetworkStream Stream;
        }

        private enum ScriptKind
        {
            Silent,
            Error,
            Custom,
            Token,
        }

        private sealed class Script
        {
            public ScriptKind Kind;
            public Error Error;
            public Command Command;
            public byte[] Payload;
            public string Token;
        }

        private sealed class Reply
        {
            public Command Command;
            public byte[] Payload;
            public string Token;
            public byte? Tag;
        }

        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _stop = new CancellationTokenSource();
        private readonly Task _acceptLoop;
        private readonly object _sync = new object();
        private readonly SemaphoreSlim _writeGate = new SemaphoreSlim(1, 1);
        private readonly List<FakeRequest> _requests = new List<FakeRequest>();
        private readonly Dictionary<int, Queue<Script>> _scripts = new Dictionary<int, Queue<Script>>();
        private readonly Dictionary<int, Queue<byte[]>> _transitions = new Dictionary<int, Queue<byte[]>>();
        private readonly Dictionary<int, Func<FakeRequest, List<Reply>, Reply>> _handlers = new Dictionary<int, Func<FakeRequest, List<Reply>, Reply>>();

        private Connection _connection;
        private int _connectionCount;
        private FakeUser _sessionUser;
        private string _sessionToken = NoToken;
        private uint _lastToken = 0xA1B2C300;
        private Exception _fault;

        public FakeGateway()
        {
            Users = new List<FakeUser>
            {
                new FakeUser { Id = 0, Name = "admin", Password = "admin-secret" },
                new FakeUser { Id = 3, Name = "anna", Password = "anna-secret", Groups = new List<int> { 1 } },
            };

            Ports = new SortedDictionary<int, int>
            {
                [0] = (int)PortType.Impuls,
                [1] = (int)PortType.Impuls,
                [2] = (int)PortType.Up,
            };

            Groups = new List<FakeGroup>
            {
                new FakeGroup { Id = 0, Name = "Garage", Ports = new List<int> { 0 } },
                new FakeGroup { Id = 1, Name = "Gate", Ports = new List<int> { 1, 2 } },
            };

            Values = new SortedDictionary<int, int>();
            for (int key = 0; key < GatewayLimits.MaxValues; key++)
                Values[key] = 0;

            Values[0] = (int)GroupType.SectionalDoor;
            Values[1] = (int)GroupType.SlidingGate;
            Values[16] = 0;
            Values[17] = 1;

            RegisterHandlers();

            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            _acceptLoop = Task.Run(AcceptLoopAsync);
        }

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        public List<FakeUser> Users { get; }
        public List<FakeGroup> Groups { get; }

        /// <summary>Port id to port type.</summary>
        public SortedDictionary<int, int> Ports { get; }

        /// <summary>The key-value store; remove a key to make GET_VALUES leave it out.</summary>
        public SortedDictionary<int, int> Values { get; }

        public string Name { get; set; } = "Garage GW";

        /// <summary>Null makes the gateway answer GET_GW_VERSION with "unknown command", like firmware up to EE001425-06.</summary>
        public string FirmwareVersion { get; set; } = "EE001425-10";

        public byte WifiState { get; set; }
        public List<string> WifiNetworks { get; } = new List<string>();

        /// <summary>
        /// Which tag the WIFI_FOUND packages of a scan carry: 0xFF when false, the tag of the SCAN_WIFI request when true.
        /// The legacy app works with either, so the real gateway's choice is not known.
        /// </summary>
        public bool WifiFoundUsesRequestTag { get; set; }

        public SetStateReply SetStateReply { get; set; } = SetStateReply.Transition;

        /// <summary>The token of the current session, "00000000" when nobody is logged in.</summary>
        public string SessionToken
        {
            get
            {
                lock (_sync)
                    return _sessionToken;
            }
        }

        public int? SessionUserId
        {
            get
            {
                lock (_sync)
                    return _sessionUser?.Id;
            }
        }

        public int ConnectionCount
        {
            get
            {
                lock (_sync)
                    return _connectionCount;
            }
        }

        /// <summary>Every request received since the start or the last <see cref="ClearRequests"/>, in order.</summary>
        public IReadOnlyList<FakeRequest> Requests
        {
            get
            {
                lock (_sync)
                    return _requests.ToArray();
            }
        }

        public IEnumerable<string> RequestNames => Requests.Select(r => r.Command.Name);

        public IReadOnlyList<FakeRequest> RequestsFor(Command command)
        {
            return Requests.Where(r => r.Command.Code == command.Code).ToArray();
        }

        public void ClearRequests()
        {
            lock (_sync)
                _requests.Clear();
        }

        public FakeUser User(string name)
        {
            return Users.Single(u => u.Name == name);
        }

        public FakeUser AddUser(string name, string password, params int[] groups)
        {
            FakeUser user = new FakeUser { Id = FreeId(Users.Select(u => u.Id)), Name = name, Password = password, Groups = groups.ToList() };
            Users.Add(user);

            return user;
        }

        /// <summary>
        /// A transition payload without the optional HCP block (gk high byte 0xFC), so the library's
        /// HCP post-processing does not alter the drive time.
        /// </summary>
        public static byte[] TransitionPayload(int actual, int requested, int driveTimeSeconds, bool autoClose = false, bool error = false)
        {
            int flags = (error ? 0x80 : 0) | (autoClose ? 0x40 : 0);

            return new byte[]
            {
                (byte)actual, (byte)requested,
                (byte)(flags | ((driveTimeSeconds >> 8) & 0x0F)), (byte)driveTimeSeconds,
                0xFC, 0x00,
                0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08,
            };
        }

        /// <summary>
        /// Scripts the transitions a port reports, one per HM_GET_TRANSITION or SET_STATE; the last one is repeated.
        /// A port without a script reports a settled, closed drive.
        /// </summary>
        public void SetTransitions(int portId, params byte[][] payloads)
        {
            lock (_sync)
                _transitions[portId] = new Queue<byte[]>(payloads);
        }

        /// <summary>The next <paramref name="times"/> requests for the command are answered with this error and not carried out.</summary>
        public void FailNext(Command command, Error error, int times = 1)
        {
            for (int i = 0; i < times; i++)
                AddScript(command, new Script { Kind = ScriptKind.Error, Error = error });
        }

        /// <summary>The next <paramref name="times"/> requests for the command get no answer at all and are not carried out.</summary>
        public void IgnoreNext(Command command, int times = 1)
        {
            for (int i = 0; i < times; i++)
                AddScript(command, new Script { Kind = ScriptKind.Silent });
        }

        /// <summary>The next request for the command is answered with this response instead of the regular one.</summary>
        public void ReplyNext(Command command, Command replyCommand, params byte[] payload)
        {
            AddScript(command, new Script { Kind = ScriptKind.Custom, Command = replyCommand, Payload = payload });
        }

        /// <summary>The next request for the command is carried out, but its answer carries this token in the header.</summary>
        public void AnswerNextWithToken(Command command, string token)
        {
            AddScript(command, new Script { Kind = ScriptKind.Token, Token = token });
        }

        /// <summary>Makes the gateway forget the session, so authenticated requests are denied until the next login.</summary>
        public void ExpireSession()
        {
            lock (_sync)
            {
                _sessionUser = null;
                _sessionToken = NoToken;
            }
        }

        /// <summary>
        /// Sends a package the client did not ask for. The token defaults to the session token.
        /// Assumption: pushed packages have the response bit set like every other package from the gateway.
        /// </summary>
        public Task PushAsync(Command command, byte[] payload = null, byte tag = BroadcastTag, string token = null, bool isResponse = true)
        {
            return WriteRawAsync(Frame(command, payload, tag, token ?? SessionToken, isResponse));
        }

        /// <summary>Writes text to the stream as is, for malformed-frame tests.</summary>
        public async Task WriteRawAsync(string text)
        {
            Connection connection = await WaitForConnectionAsync().ConfigureAwait(false);
            byte[] bytes = Encoding.ASCII.GetBytes(text);

            await _writeGate.WaitAsync().ConfigureAwait(false);
            try
            {
                await connection.Stream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
            }
            finally
            {
                _writeGate.Release();
            }
        }

        /// <summary>The hex text of one package from the gateway to the client.</summary>
        public static string Frame(Command command, byte[] payload, byte tag, string token = NoToken, bool isResponse = true)
        {
            Message message = new Message(command, tag, token, new Payload(payload ?? new byte[0]), isResponse, 0);
            Package package = new Package(GatewayId, AssignedClientId, message);

            return StringHelper.HexStringFromByteArray(package.ToByteArray());
        }

        /// <summary>
        /// Closes the current connection from the gateway's side: an orderly close, or a reset when <paramref name="reset"/> is true.
        /// </summary>
        public void DropConnection(bool reset = false)
        {
            Connection connection;
            lock (_sync)
            {
                connection = _connection;
                _connection = null;
            }

            if (connection == null)
                return;

            if (reset)
                connection.Client.LingerState = new LingerOption(true, 0);

            connection.Client.Close();
        }

        /// <summary>
        /// Waits until the given number of requests for the command has arrived and returns that request.
        /// </summary>
        public async Task<FakeRequest> WaitForRequestAsync(Command command, int occurrence = 1)
        {
            DateTime deadline = DateTime.UtcNow + WaitLimit;
            while (true)
            {
                IReadOnlyList<FakeRequest> matches = RequestsFor(command);
                if (matches.Count >= occurrence)
                    return matches[occurrence - 1];

                if (DateTime.UtcNow > deadline)
                    throw new TimeoutException($"The fake gateway did not receive request {occurrence} for {command.Name}.");

                await Task.Delay(5).ConfigureAwait(false);
            }
        }

        public async Task WaitForConnectionCountAsync(int count)
        {
            DateTime deadline = DateTime.UtcNow + WaitLimit;
            while (ConnectionCount < count)
            {
                if (DateTime.UtcNow > deadline)
                    throw new TimeoutException($"The fake gateway did not get connection {count}.");

                await Task.Delay(5).ConfigureAwait(false);
            }
        }

        public void Dispose()
        {
            _stop.Cancel();
            _listener.Stop();
            DropConnection();
            _acceptLoop.Wait(TimeSpan.FromSeconds(5));
            _stop.Dispose();

            // A fault inside the fake would otherwise only show up as a timeout in the test.
            if (_fault != null)
                throw new InvalidOperationException("The fake gateway failed while handling a request: " + _fault.Message, _fault);
        }

        private async Task<Connection> WaitForConnectionAsync()
        {
            DateTime deadline = DateTime.UtcNow + WaitLimit;
            while (true)
            {
                lock (_sync)
                {
                    if (_connection != null)
                        return _connection;
                }

                if (DateTime.UtcNow > deadline)
                    throw new TimeoutException("No client is connected to the fake gateway.");

                await Task.Delay(5).ConfigureAwait(false);
            }
        }

        private void AddScript(Command command, Script script)
        {
            lock (_sync)
            {
                if (!_scripts.TryGetValue(command.Code, out Queue<Script> queue))
                    _scripts[command.Code] = queue = new Queue<Script>();

                queue.Enqueue(script);
            }
        }

        private async Task AcceptLoopAsync()
        {
            while (!_stop.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync(_stop.Token).ConfigureAwait(false);
                }
                catch (Exception e) when (e is OperationCanceledException || e is ObjectDisposedException || e is SocketException)
                {
                    return;
                }

                client.NoDelay = true;
                Connection connection = new Connection { Client = client, Stream = client.GetStream() };

                lock (_sync)
                {
                    _connection = connection;
                    _connectionCount++;

                    // Assumption: a session does not survive its connection.
                    _sessionUser = null;
                    _sessionToken = NoToken;
                }

                _ = Task.Run(() => ServeAsync(connection));
            }
        }

        private async Task ServeAsync(Connection connection)
        {
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    byte[] frame = await ReadFrameAsync(connection.Stream, _stop.Token).ConfigureAwait(false);
                    await HandleAsync(connection, Package.Load(frame)).ConfigureAwait(false);
                }
            }
            catch (Exception e) when (e is IOException || e is ObjectDisposedException || e is OperationCanceledException || e is SocketException)
            {
                // The client went away or the test dropped the connection.
            }
            catch (Exception e)
            {
                _fault = e;
            }
            finally
            {
                connection.Client.Dispose();
            }
        }

        private static async Task<byte[]> ReadFrameAsync(Stream stream, CancellationToken cancellationToken)
        {
            int headerSize = Lengths.ADDRESS_SIZE + Lengths.ADDRESS_SIZE + Lengths.LENGTH_SIZE;
            byte[] header = new byte[headerSize];
            await stream.ReadExactlyAsync(header, 0, headerSize, cancellationToken).ConfigureAwait(false);

            int length = Convert.ToInt32(Encoding.ASCII.GetString(header, headerSize - Lengths.LENGTH_SIZE, Lengths.LENGTH_SIZE), 16);
            int remaining = (length - Lengths.LENGTH_BYTES + Lengths.CHECKSUM_BYTES) * Lengths.BYTE_LENGTH;

            byte[] frame = new byte[headerSize + remaining];
            Array.Copy(header, frame, headerSize);
            await stream.ReadExactlyAsync(frame, headerSize, remaining, cancellationToken).ConfigureAwait(false);

            return frame;
        }

        private async Task HandleAsync(Connection connection, Package package)
        {
            Message message = package.Message;
            FakeRequest request = new FakeRequest
            {
                Command = message.Command,
                Payload = message.Payload.ToByteArray(),
                Sender = package.Sender,
                Receiver = package.Receiver,
                Tag = message.Tag,
                Token = message.Token,
                IsResponse = message.IsResponse,
                ChecksumsValid = package.IsCorrectChecksum && message.Checksum == message.CalculatedChecksum,
            };

            List<Reply> replies = new List<Reply>();
            lock (_sync)
            {
                _requests.Add(request);

                Script script = null;
                if (_scripts.TryGetValue(request.Command.Code, out Queue<Script> queue) && queue.Count > 0)
                    script = queue.Dequeue();

                if (script != null && script.Kind == ScriptKind.Silent)
                    return;

                Reply reply;
                if (script != null && script.Kind == ScriptKind.Error)
                    reply = Fail(script.Error);
                else if (script != null && script.Kind == ScriptKind.Custom)
                    reply = new Reply { Command = script.Command, Payload = script.Payload, Token = _sessionToken };
                else
                    reply = Process(request, replies);

                if (script != null && script.Kind == ScriptKind.Token)
                    reply.Token = script.Token;

                replies.Add(reply);
            }

            StringBuilder text = new StringBuilder();
            foreach (Reply reply in replies)
                text.Append(Frame(reply.Command, reply.Payload, reply.Tag ?? request.Tag, reply.Token));

            byte[] bytes = Encoding.ASCII.GetBytes(text.ToString());

            await _writeGate.WaitAsync().ConfigureAwait(false);
            try
            {
                await connection.Stream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
            }
            finally
            {
                _writeGate.Release();
            }
        }

        private Reply Process(FakeRequest request, List<Reply> pushes)
        {
            if (request.Command.AuthenticationRequired && (_sessionUser == null || request.Token != _sessionToken))
                return Fail(Error.PermissionDenied);

            if (!_handlers.TryGetValue(request.Command.Code, out Func<FakeRequest, List<Reply>, Reply> handler))
                return Fail(Error.CommandNotFound);

            return handler(request, pushes);
        }

        private Reply Ok(Command command, params byte[] payload)
        {
            return new Reply { Command = command, Payload = payload, Token = _sessionToken };
        }

        private Reply Ok(Command command, string text)
        {
            return Ok(command, Encoding.UTF8.GetBytes(text));
        }

        private Reply Fail(Error error)
        {
            return Ok(Command.Error, (byte)error);
        }

        private void On(Command command, Func<FakeRequest, Reply> handler)
        {
            _handlers[command.Code] = (request, pushes) => handler(request);
        }

        // Acknowledgements the legacy app does not read are answered without payload (assumption).
        private void RegisterHandlers()
        {
            On(Command.Ping, r => Ok(Command.Ping));
            On(Command.Login, Login);
            On(Command.Logout, Logout);
            On(Command.Jmcp, Jmcp);
            On(Command.GetName, r => Ok(Command.GetName, Name));
            On(Command.SetName, r =>
            {
                Name = r.Text;
                return Ok(Command.SetName);
            });
            On(Command.GetGatewayVersion, r => FirmwareVersion == null ? Fail(Error.CommandNotFound) : Ok(Command.GetGatewayVersion, FirmwareVersion));
            On(Command.GetWifiState, r => Ok(Command.GetWifiState, WifiState));
            _handlers[Command.ScanWifi.Code] = ScanWifi;

            On(Command.SetValue, r =>
            {
                if (r.Payload.Length != 2)
                    return Fail(Error.InvalidPayload);

                if (r.Payload[0] >= GatewayLimits.MaxValues)
                    return Fail(Error.OutOfRange);

                Values[r.Payload[0]] = r.Payload[1];
                return Ok(Command.SetValue);
            });

            On(Command.AddUser, AddUser);
            On(Command.RemoveUser, r => WithUser(r, 1, user =>
            {
                Users.Remove(user);
                return Ok(Command.RemoveUser);
            }));
            On(Command.GetUserRights, r => WithUser(r, 1, user =>
                Ok(Command.GetUserRights, new[] { (byte)user.Id }.Concat(user.Groups.Select(g => (byte)g)).ToArray())));
            On(Command.SetUserRights, r => WithUser(r, 1, user =>
            {
                user.Groups = r.Payload.Skip(1).Select(b => (int)b).ToList();
                return Ok(Command.SetUserRights);
            }));
            On(Command.ChangeUserName, r =>
            {
                _sessionUser.Name = r.Text;
                return Ok(Command.ChangeUserName);
            });
            On(Command.ChangePassword, r =>
            {
                _sessionUser.Password = r.Text;
                return Ok(Command.ChangePassword);
            });
            On(Command.ChangeUserNameOfUser, r => AsAdmin(() => WithUser(r, 1, user =>
            {
                user.Name = Encoding.UTF8.GetString(r.Payload, 1, r.Payload.Length - 1);
                return Ok(Command.ChangeUserNameOfUser);
            })));
            On(Command.ChangePasswordOfUser, r => AsAdmin(() => WithUser(r, 1, user =>
            {
                user.Password = Encoding.UTF8.GetString(r.Payload, 1, r.Payload.Length - 1);
                return Ok(Command.ChangePasswordOfUser);
            })));

            On(Command.AddGroup, r =>
            {
                if (Groups.Count >= GatewayLimits.MaxPorts)
                    return Fail(Error.NoEmptyGroupSlot);

                FakeGroup group = new FakeGroup { Id = FreeId(Groups.Select(g => g.Id)), Name = "" };
                Groups.Add(group);
                return Ok(Command.AddGroup, (byte)group.Id);
            });
            On(Command.RemoveGroup, r => WithGroup(r, 1, group =>
            {
                Groups.Remove(group);
                return Ok(Command.RemoveGroup);
            }));
            On(Command.SetGroupName, r => WithGroup(r, 1, group =>
            {
                group.Name = Encoding.UTF8.GetString(r.Payload, 1, r.Payload.Length - 1);
                return Ok(Command.SetGroupName);
            }));
            On(Command.SetGroupedPorts, r => WithGroup(r, 1, group =>
            {
                group.Ports = r.Payload.Skip(1).Select(b => (int)b).ToList();
                return Ok(Command.SetGroupedPorts);
            }));

            On(Command.GetPorts, r => Ok(Command.GetPorts, Ports.Keys.Select(id => (byte)id).ToArray()));
            On(Command.AddPort, r => CreatePort(Command.AddPort));
            On(Command.InheritPort, r => CreatePort(Command.InheritPort));
            On(Command.SetPortType, r => WithPort(r, 2, portId =>
            {
                Ports[portId] = r.Payload[1];
                return Ok(Command.SetPortType);
            }));
            On(Command.RemovePort, r => WithPort(r, 1, portId =>
            {
                Ports.Remove(portId);
                foreach (FakeGroup group in Groups)
                    group.Ports.Remove(portId);

                return Ok(Command.RemovePort, (byte)portId);
            }));

            On(Command.GetTransition, r => WithPort(r, 1, portId => Ok(Command.GetTransition, NextTransition(portId))));
            On(Command.SetState, r => WithPort(r, 2, portId => SetStateReply == SetStateReply.Acknowledge
                ? Ok(Command.SetState)
                : Ok(Command.GetTransition, NextTransition(portId))));
        }

        private Reply Login(FakeRequest request)
        {
            _sessionUser = null;
            _sessionToken = NoToken;

            byte[] payload = request.Payload;
            if (payload.Length < 1 || payload.Length < 1 + payload[0])
                return Fail(Error.InvalidPayload);

            // LOGIN request payload: [name length in UTF-8 bytes][name][password]
            string name = Encoding.UTF8.GetString(payload, 1, payload[0]);
            string password = Encoding.UTF8.GetString(payload, 1 + payload[0], payload.Length - 1 - payload[0]);

            FakeUser user = Users.FirstOrDefault(u => u.Name == name && u.Password == password);
            if (user == null)
                return Fail(Error.LoginFailed);

            _lastToken++;
            byte[] token = ConversionHelper.IntToByteArray(unchecked((int)_lastToken), 4);

            _sessionUser = user;
            _sessionToken = StringHelper.HexStringFromByteArray(token);

            // The login response itself still carries a zero token in its header.
            return new Reply
            {
                Command = Command.Login,
                Payload = new[] { (byte)user.Id }.Concat(token).ToArray(),
                Token = NoToken,
            };
        }

        private Reply Logout(FakeRequest request)
        {
            Reply reply = Ok(Command.Logout);
            _sessionUser = null;
            _sessionToken = NoToken;

            return reply;
        }

        private Reply Jmcp(FakeRequest request)
        {
            string cmd;
            int? forUser = null;
            try
            {
                using (JsonDocument document = JsonDocument.Parse(request.Text))
                {
                    cmd = document.RootElement.GetProperty("cmd").GetString();
                    if (document.RootElement.TryGetProperty("forUser", out JsonElement user))
                        forUser = user.GetInt32();
                }
            }
            catch (Exception e) when (e is JsonException || e is KeyNotFoundException || e is InvalidOperationException)
            {
                return Fail(Error.InvalidPayload);
            }

            JsonSerializerOptions options = new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

            switch (cmd)
            {
                case "GET_USERS":
                    return Ok(Command.Jmcp, JsonSerializer.Serialize(
                        Users.Select(u => new { id = u.Id, name = u.Name, isAdmin = u.Id == GatewayLimits.AdminUserId, groups = u.Groups }), options));

                case "GET_GROUPS":
                    IEnumerable<FakeGroup> groups = Groups;
                    if (forUser != null)
                    {
                        FakeUser user = Users.FirstOrDefault(u => u.Id == forUser);
                        if (user == null)
                            return Fail(Error.UserNotFound);

                        groups = Groups.Where(g => user.Groups.Contains(g.Id));
                    }

                    return Ok(Command.Jmcp, JsonSerializer.Serialize(
                        groups.Select(g => new
                        {
                            id = g.Id,
                            name = g.Name,
                            ports = g.Ports.Select(p => new { id = p, type = Ports.TryGetValue(p, out int type) ? type : 0 }),
                        }), options));

                case "GET_VALUES":
                    return Ok(Command.Jmcp, "{" + string.Join(",", Values.Select(v => $"\"{v.Key:00}\":{v.Value}")) + "}");

                default:
                    return Fail(Error.InvalidPayload);
            }
        }

        private Reply ScanWifi(FakeRequest request, List<Reply> pushes)
        {
            // The gateway reports every network in its own WIFI_FOUND package before it answers SCAN_WIFI.
            foreach (string ssid in WifiNetworks)
            {
                pushes.Add(new Reply
                {
                    Command = Command.WifiFound,
                    Payload = new byte[] { 0x01 }.Concat(Encoding.UTF8.GetBytes(ssid)).ToArray(),
                    Token = _sessionToken,
                    Tag = WifiFoundUsesRequestTag ? request.Tag : BroadcastTag,
                });
            }

            return Ok(Command.ScanWifi);
        }

        private Reply AddUser(FakeRequest request)
        {
            byte[] payload = request.Payload;
            if (payload.Length < 1 || payload.Length < 1 + payload[0])
                return Fail(Error.InvalidPayload);

            string name = Encoding.UTF8.GetString(payload, 1, payload[0]);
            string password = Encoding.UTF8.GetString(payload, 1 + payload[0], payload.Length - 1 - payload[0]);

            if (Users.Any(u => u.Name == name))
                return Fail(Error.UserAlreadyExists);

            if (Users.Count >= GatewayLimits.MaxUsers)
                return Fail(Error.NoEmptyUserSlot);

            FakeUser user = AddUser(name, password);

            return Ok(Command.AddUser, (byte)user.Id);
        }

        private Reply CreatePort(Command command)
        {
            if (Ports.Count >= GatewayLimits.MaxPorts)
                return Fail(Error.NoEmptyPortSlot);

            int portId = FreeId(Ports.Keys);
            Ports[portId] = (int)PortType.None;

            return Ok(command, (byte)portId);
        }

        private Reply AsAdmin(Func<Reply> action)
        {
            return _sessionUser.Id == GatewayLimits.AdminUserId ? action() : Fail(Error.PermissionDenied);
        }

        private Reply WithUser(FakeRequest request, int minimumLength, Func<FakeUser, Reply> action)
        {
            if (request.Payload.Length < minimumLength)
                return Fail(Error.InvalidPayload);

            FakeUser user = Users.FirstOrDefault(u => u.Id == request.Payload[0]);

            return user == null ? Fail(Error.UserNotFound) : action(user);
        }

        private Reply WithGroup(FakeRequest request, int minimumLength, Func<FakeGroup, Reply> action)
        {
            if (request.Payload.Length < minimumLength)
                return Fail(Error.InvalidPayload);

            FakeGroup group = Groups.FirstOrDefault(g => g.Id == request.Payload[0]);

            return group == null ? Fail(Error.GroupNotFound) : action(group);
        }

        private Reply WithPort(FakeRequest request, int minimumLength, Func<int, Reply> action)
        {
            if (request.Payload.Length < minimumLength)
                return Fail(Error.InvalidPayload);

            return Ports.ContainsKey(request.Payload[0]) ? action(request.Payload[0]) : Fail(Error.PortNotFound);
        }

        private byte[] NextTransition(int portId)
        {
            if (!_transitions.TryGetValue(portId, out Queue<byte[]> queue) || queue.Count == 0)
                return TransitionPayload(0, 0, 0);

            return queue.Count > 1 ? queue.Dequeue() : queue.Peek();
        }

        private static int FreeId(IEnumerable<int> used)
        {
            HashSet<int> taken = new HashSet<int>(used);
            int id = 0;
            while (taken.Contains(id))
                id++;

            return id;
        }
    }
}
