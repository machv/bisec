using BiSec.Library.Converters;
using BiSec.Library.Exceptions;
using BiSec.Library.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

[assembly: InternalsVisibleTo("bisec-tests")]
namespace BiSec.Library
{
    /// <summary>
    /// High-level API of a BiSecur gateway. Calls throw <see cref="GatewayException"/> when the gateway
    /// answers with an error, <see cref="GatewayProtocolException"/> when the answer is not what the
    /// protocol allows, and <see cref="TimeoutException"/> when it does not answer in time.
    /// </summary>
    public partial class GatewayClient : IDisposable
    {
        private ILogger _logger;
        private readonly GatewayConnection _gatewayConnection;
        private string _userName;
        private string _password;
        private int? _userId;
        private Dictionary<int, int> _values;
        private GatewayVersion _version;

        public GatewayConnection Connection => _gatewayConnection;

        /// <summary>The gateway's id of the logged-in user, or null when not logged in.</summary>
        public int? UserId => _userId;
        public bool IsLoggedIn => _userId.HasValue && _gatewayConnection.IsAuthenticated;
        public bool IsAdmin => _userId == GatewayLimits.AdminUserId;

        /// <summary>
        /// When the gateway answers PERMISSION_DENIED, log in again with the last credentials and
        /// repeat the request once. The legacy app re-logs in on that error as well.
        /// </summary>
        public bool AutoRelogin { get; set; } = true;

        /// <summary>
        /// The legacy app only offers some operations from a certain firmware on (editing other
        /// users needs EE001425-10). Set to false to send those requests regardless.
        /// </summary>
        public bool EnforceFirmwareFeatureGates { get; set; } = true;

        /// <summary>
        /// Raised when the session ends without <see cref="LogoutAsync"/>: the gateway pushed a
        /// logout or the connection dropped.
        /// </summary>
        public event EventHandler SessionEnded;

        public GatewayClient(GatewayConnection gatewayConnection)
        {
            _gatewayConnection = gatewayConnection ?? throw new ArgumentNullException(nameof(gatewayConnection));
            _gatewayConnection.LoggedOut += OnSessionEnded;
            _gatewayConnection.Disconnected += OnSessionEnded;

            if (!_gatewayConnection.Connected)
                _gatewayConnection.Connect();
        }

        public GatewayClient(DiscoveryResult discoveryResult, ILogger logger) : this(CreateConnection(discoveryResult, logger))
        {
            SetLogger(logger);
        }

        public GatewayClient(DiscoveryResult discoveryResult) : this(discoveryResult, null) { }

        private static GatewayConnection CreateConnection(DiscoveryResult discoveryResult, ILogger logger)
        {
            if (discoveryResult == null)
                throw new ArgumentNullException(nameof(discoveryResult));

            GatewayConnection connection = new GatewayConnection(discoveryResult);
            connection.SetLogger(logger);

            return connection;
        }

        public void SetLogger(ILogger logger)
        {
            _logger = logger;
        }

        public void Disconnect()
        {
            _gatewayConnection.Disconnect();
        }

        public void Dispose()
        {
            _gatewayConnection.LoggedOut -= OnSessionEnded;
            _gatewayConnection.Disconnected -= OnSessionEnded;
            _gatewayConnection.Dispose();
            ClearSession();
        }

        private void OnSessionEnded(object sender, EventArgs e)
        {
            bool wasLoggedIn = _userId.HasValue;
            ClearSession();

            if (wasLoggedIn)
                SessionEnded?.Invoke(this, EventArgs.Empty);
        }

        private void ClearSession()
        {
            _userId = null;
            _values = null;
            _version = null;
        }

        public async Task<bool> PingAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            await RequestAsync(Command.Ping, PayloadFactory.Empty, cancellationToken).ConfigureAwait(false);

            return true;
        }

        /// <summary>
        /// Logs in and returns the user's id and session token. Throws <see cref="GatewayException"/>
        /// with <see cref="Error.LoginFailed"/> for wrong credentials or <see cref="Error.GatewayBusy"/>.
        /// </summary>
        public async Task<LoginResult> LoginAsync(string userName, string password, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (userName == null)
                throw new ArgumentNullException(nameof(userName));

            if (password == null)
                throw new ArgumentNullException(nameof(password));

            ClearSession();

            Message response = await SendAndCheckAsync(Command.Login, PayloadFactory.Login(userName, password), cancellationToken).ConfigureAwait(false);

            // LOGIN response payload: [userId][token:4]
            byte[] data = RequirePayload(response, Command.Login, 5);

            _userName = userName;
            _password = password;
            _userId = data[0];

            return new LoginResult(data[0], _gatewayConnection.Token);
        }

        /// <summary>
        /// Logs in and returns the session token.
        /// </summary>
        public async Task<string> GetToken(string userName, string password)
        {
            LoginResult result = await LoginAsync(userName, password).ConfigureAwait(false);

            return result.Token;
        }

        /// <summary>
        /// Ends the session and forgets the stored credentials.
        /// </summary>
        public async Task LogoutAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            try
            {
                if (_gatewayConnection.Connected && _gatewayConnection.IsAuthenticated)
                    await SendAndCheckAsync(Command.Logout, PayloadFactory.Empty, cancellationToken).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                // The legacy app does not wait for an answer to LOGOUT either.
                _logger?.LogInformation("The gateway did not answer LOGOUT; the session is dropped locally.");
            }
            finally
            {
                _gatewayConnection.ClearToken();
                _userName = null;
                _password = null;
                ClearSession();
            }
        }

        /// <summary>
        /// Sends any command and returns the response as is, including ERROR responses.
        /// Meant for commands whose payloads this library does not model.
        /// </summary>
        public async Task<Message> SendRawAsync(Command command, Payload payload = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (command == null)
                throw new ArgumentNullException(nameof(command));

            return await SendOnceAsync(command, payload ?? PayloadFactory.Empty, cancellationToken).ConfigureAwait(false);
        }

        private async Task<Message> SendOnceAsync(Command command, Payload payload, CancellationToken cancellationToken)
        {
            Message message = new Message
            {
                Command = command,
                Payload = payload,
            };

            Package response = await _gatewayConnection.SendAsync(message, cancellationToken).ConfigureAwait(false);

            return response.Message;
        }

        /// <summary>
        /// Sends a request and returns the response once it is known to be the expected one.
        /// </summary>
        private async Task<Message> RequestAsync(Command command, Payload payload, CancellationToken cancellationToken, params Command[] alsoAccepted)
        {
            Message response = await SendOnceAsync(command, payload, cancellationToken).ConfigureAwait(false);

            if (response.GetError() == Error.PermissionDenied && AutoRelogin && _userName != null && _password != null)
            {
                _logger?.LogInformation($"{command.Name} was denied; logging in again as the legacy app does.");

                await LoginAsync(_userName, _password, cancellationToken).ConfigureAwait(false);
                response = await SendOnceAsync(command, payload, cancellationToken).ConfigureAwait(false);
            }

            return CheckResponse(command, response, alsoAccepted);
        }

        private async Task<Message> SendAndCheckAsync(Command command, Payload payload, CancellationToken cancellationToken)
        {
            Message response = await SendOnceAsync(command, payload, cancellationToken).ConfigureAwait(false);

            return CheckResponse(command, response);
        }

        private static Message CheckResponse(Command command, Message response, params Command[] alsoAccepted)
        {
            if (response.IsError)
            {
                Error? error = response.GetError();
                if (error == null)
                    throw new GatewayProtocolException($"The gateway answered {command.Name} with an ERROR that carries no error code.");

                throw new GatewayException(command, error.Value);
            }

            if (response.Command.Code == command.Code)
                return response;

            foreach (Command accepted in alsoAccepted)
            {
                if (response.Command.Code == accepted.Code)
                    return response;
            }

            throw new GatewayProtocolException($"The gateway answered {command.Name} with unexpected {response.Command.Name}.");
        }

        private static byte[] RequirePayload(Message response, Command command, int minimumLength)
        {
            byte[] data = response.Payload?.ToByteArray() ?? new byte[0];
            if (data.Length < minimumLength)
                throw new GatewayProtocolException($"The {command.Name} response carries {data.Length} payload bytes, expected at least {minimumLength}.");

            return data;
        }

        private void RequireLogin()
        {
            if (!IsLoggedIn)
                throw new InvalidOperationException("This operation needs a logged-in user. Call LoginAsync first.");
        }

        internal static T ParseJsonResponse<T>(string json)
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            };
            options.Converters.Add(new DictionaryIntKeyConverter());

            try
            {
                return JsonSerializer.Deserialize<T>(json, options);
            }
            catch (JsonException e)
            {
                throw new GatewayProtocolException("The gateway returned JSON that could not be parsed.", e);
            }
        }
    }
}
