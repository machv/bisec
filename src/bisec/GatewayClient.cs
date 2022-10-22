using BiSec.Library.Models;
using BiSec.Library.Converters;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

[assembly: InternalsVisibleTo("bisec-tests")]
namespace BiSec.Library
{
    public class GatewayClient
    {
        private const int MaxErrorRetryCount = 5;
        private int _errorRetryCount = 0;

        private ILogger _logger;
        private GatewayConnection _gatewayConnection;
        private byte _tag;
        private string _userName;
        private string _password;
        private Dictionary<int, GroupType> _groupTypes;

        public GatewayClient(GatewayConnection gatewayConnection)
        {
            _gatewayConnection = gatewayConnection;
            if (!_gatewayConnection.Connected)
                _gatewayConnection.Connect();
        }

        public GatewayClient(DiscoveryResult discoveryResult, ILogger logger)
        {
            SetLogger(logger);

            _gatewayConnection = new GatewayConnection(discoveryResult);
            _gatewayConnection.SetLogger(logger);
            _gatewayConnection.Connect();
        }

        public GatewayClient(DiscoveryResult discoveryResult) : this(discoveryResult, null) { }

        public void SetLogger(ILogger logger)
        {
            _logger = logger;
        }

        public void Disconnect()
        {
            _gatewayConnection.Disconnect();
        }

        public async Task<bool> PingAsync()
        {
            Message message = new Message
            {
                Command = Command.Ping,
                Payload = PayloadFactory.Empty,
                Tag = GetNewTag(),
            };

            _gatewayConnection.Send(message);
            var response = await _gatewayConnection.GetResponseAsync(message.Tag);

            return response.Message.Command == Command.Ping;
        }

        public async Task<string> GetGatewayVersionAsync()
        {
            Message message = new Message
            {
                Command = Command.GetGatewayVersion,
                Payload = PayloadFactory.Empty,
                Tag = GetNewTag(),
            };

            _gatewayConnection.Send(message);
            var response = await _gatewayConnection.GetResponseAsync(message.Tag);

            return response.Message.Payload.TextContent;
        }

        public async Task<string> GetNameAsync()
        {
            Message message = new Message
            {
                Command = Command.GetName,
                Payload = PayloadFactory.Empty,
                Tag = GetNewTag()
            };

            _gatewayConnection.Send(message);
            var response = await _gatewayConnection.GetResponseAsync(message.Tag);

            return response.Message.Payload.TextContent;
        }

        public async Task<bool> LoginAsync(string userName, string password)
        {
            Message message = new Message
            {
                Command = Command.Login,
                Payload = PayloadFactory.Login(userName, password),
                Tag = GetNewTag()
            };

            _gatewayConnection.Send(message);
            var response = await _gatewayConnection.GetResponseAsync(message.Tag);

            if (response.Message.Command == Command.Login)
            {
                _userName = userName;
                _password = password;

                return true;
            }
            else if (response.Message.Command == Command.Error)
            {
                var error = response.Message.GetError();

                _logger?.LogError($"Received error {error} from gateway in response to Login command.");
            }
            else
            {
                // panic?
            }

            return false;
        }

        public async Task<bool> ScanWifiAsync()
        {
            Message message = new Message
            {
                Command = Command.SCAN_WIFI,
                Payload = PayloadFactory.Empty,
                Tag = GetNewTag(),
            };

            _gatewayConnection.Send(message);
            var response = await _gatewayConnection.GetResponseAsync(message.Tag);

            return response.Message.Command == Command.Ping;
        }

        public async Task<bool> GetWifiStateAsync()
        {
            Message message = new Message
            {
                Command = Command.GET_WIFI_STATE,
                Payload = PayloadFactory.Empty,
                Tag = GetNewTag(),
            };

            _gatewayConnection.Send(message);
            var response = await _gatewayConnection.GetResponseAsync(message.Tag);

            return response.Message.Command == Command.Ping;
        }
        /*
        public async Task<object> GetStateAsync()
        {
            BiSecurMessage message = new BiSecurMessage
            {
                Command = Command.get
            };
        }
        */
        public async Task<User[]> GetUsersAsync()
        {
            Message message = new Message
            {
                Command = Command.Jmcp,
                Payload = PayloadFactory.GetUsers(),
                Tag = GetNewTag()
            };

            _gatewayConnection.Send(message);
            var response = await _gatewayConnection.GetResponseAsync(message.Tag);

            var json = response.Message.Payload.TextContent;
            User[] users = ParseJsonResponse<User[]>(json);

            return users;
        }

        public async Task<int> AddUserAsync(string login, string password)
        {
            Message message = new Message
            {
                Command = Command.ADD_USER,
                Payload = PayloadFactory.AddUser(login, password),
                Tag = GetNewTag()
            };

            _gatewayConnection.Send(message);
            var response = await _gatewayConnection.GetResponseAsync(message.Tag);

            var data = response.Message.Payload.ToByteArray();

            return data[0];
        }

        public async Task<bool> SetUserRightsAsync(int userId, int[] groupIds)
        {
            Message message = new Message
            {
                Command = Command.SET_USER_RIGHTS,
                Payload = PayloadFactory.SetUserRights(userId, groupIds),
                Tag = GetNewTag()
            };

            _gatewayConnection.Send(message);
            var response = await _gatewayConnection.GetResponseAsync(message.Tag);

            // return value is user ID + ids of groups assigned

            return false;
        }

        internal static T ParseJsonResponse<T>(string json)
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            };
            options.Converters.Add(new DictionaryIntKeyConverter());

            var response = JsonSerializer.Deserialize<T>(json, options);

            return response;

        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="userName"></param>
        /// <param name="password"></param>
        /// <returns></returns>
        public async Task<string> GetToken(string userName, string password)
        {
            var loginStatus = await LoginAsync(userName, password);
            if (loginStatus)
            {
                return _gatewayConnection.Token;
            }

            throw new Exception("Login failed");
        }

        /// <summary>
        ///  Returns all groups available on a BiSecur gateway with device types configured on them.
        /// </summary>
        /// <returns></returns>
        public async Task<Dictionary<int, GroupType>> GetGroupTypesAsync()
        {
            Message message = new Message
            {
                Command = Command.Jmcp,
                Payload = PayloadFactory.GetValues(),
                Tag = GetNewTag()
            };

            _gatewayConnection.Send(message);
            var response = await _gatewayConnection.GetResponseAsync(message.Tag);

            var json = response.Message.Payload.TextContent;
            var values = ParseJsonResponse<Dictionary<int, GroupType>>(json);

            // cache for other functions
            _groupTypes = values;

            return values;
        }

        private async Task<Package> GetResponseRetryAsync(Message message, int maxRetries)
        {
            if (maxRetries < 1)
                throw new Exception("At least one retry should be allowed.");

            Package response = null;
            for (int i = 0; i < maxRetries; i++)
            {
                _gatewayConnection.Send(message);
                response = await _gatewayConnection.GetResponseAsync(message.Tag);

                if (response.Message.Command != Command.Error)
                    return response;

                _logger?.LogWarning($"{message.Command} (Tag: {message.Tag}) error: {response.Message.GetError()}");
                message.Tag = GetNewTag();

                await Task.Delay(100);
            }

            return response;
        }

        public async Task<Transition> GetTransitionAsync(Port port)
        {
            Message message = new Message
            {
                Command = Command.GetTransition,
                Payload = PayloadFactory.GetTransition(port.Id),
                Tag = GetNewTag()
            };

            var response = await GetResponseRetryAsync(message, MaxErrorRetryCount);
            if (response == null)
                return null;

            if (response.Message.Command == Command.Error)
            {
                _logger?.LogWarning($"GetTransition (Port: {port.Id}) error: {response.Message.GetError()}");
                return null;
            }

            var data = response.Message.Payload.ToByteArray();
            var transition = new Transition(data);

            return transition;
        }

        public async Task SetStateAsync(Port port)
        {
            Message message = new Message
            {
                Command = Command.SetState,
                Payload = PayloadFactory.SetState(port.Id),
                Tag = GetNewTag()
            };

            _gatewayConnection.Send(message);
            var response = await _gatewayConnection.GetResponseAsync(message.Tag);
            if(response.Message.Command.Code == Command.Error.Code) {
                _logger?.LogWarning("set state got error response");
            }

            if (response.Message.Command.Code == Command.SetState.Code)
            {
                _logger?.LogInformation("Set state got set state response");
            }

            if (response.Message.Command.Code == Command.GetTransition.Code)
            {
                _logger?.LogInformation("Set state got transition");
            }
        }

        public async Task<Group[]> GetGroupsAsync(bool forUser = false)
        {
            var payload = forUser ? PayloadFactory.GetGroupsForUser() : PayloadFactory.GetGroups();

            Message message = new Message
            {
                Command = Command.Jmcp,
                Payload = payload,
                Tag = GetNewTag()
            };

            _gatewayConnection.Send(message);
            var response = await _gatewayConnection.GetResponseAsync(message.Tag);

            var json = response.Message.Payload.TextContent;
            Group[] groups = ParseJsonResponse<Group[]>(json);

            if (_groupTypes == null)
                _groupTypes = await GetGroupTypesAsync();

            foreach (var group in groups)
                group.Type = _groupTypes[group.Id];

            return groups;
        }

        private byte GetNewTag()
        {
            if (_tag >= 128)
                _tag = 0;
            else
                _tag++;

            return _tag;
        }
    }
}
