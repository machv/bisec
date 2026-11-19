using BiSec.Library;
using BiSec.Library.Exceptions;
using BiSec.Library.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BiSec.Harness
{
    /// <summary>
    /// Manual test harness for a real gateway. Read-only commands run as they are; a command that
    /// changes the gateway needs --write and a typed confirmation.
    /// </summary>
    internal static class Program
    {
        private const int ExitOk = 0;
        private const int ExitFailed = 1;
        private const int ExitUsage = 2;
        private const int ExitAborted = 3;

        private sealed class CommandInfo
        {
            public string Name;
            public string Arguments = "";
            public string Description;
            public bool NeedsGateway = true;
            public bool NeedsLogin = true;

            /// <summary>Set for commands that change the gateway; returns what is about to happen.</summary>
            public Func<Options, string> Describe;
            public Func<Session, Task> Run;

            public bool Writes => Describe != null;
        }

        private sealed class Session
        {
            public Options Options;
            public ILogger Logger;
            public GatewayClient Client;
        }

        private static readonly CommandInfo[] Commands =
        {
            new CommandInfo { Name = "discover", Description = "Broadcast a discovery request and list the gateways that answer, with their names.", NeedsGateway = false, NeedsLogin = false, Run = DiscoverAsync },
            new CommandInfo { Name = "info", Description = "Ping, gateway name and firmware version (no login).", NeedsLogin = false, Run = InfoAsync },
            new CommandInfo { Name = "users", Description = "List users and the groups each may operate.", Run = UsersAsync },
            new CommandInfo { Name = "groups", Description = "List groups with type, ports and requestable port.", Run = GroupsAsync },
            new CommandInfo { Name = "values", Description = "Dump the 64-entry key-value store.", Run = ValuesAsync },
            new CommandInfo { Name = "state", Arguments = "[groupId]", Description = "Request and interpret the state of one group or of all groups.", Run = StateAsync },
            new CommandInfo { Name = "watch", Arguments = "<groupId>", Description = "Follow a group's state until it is settled (Ctrl+C to stop).", Run = WatchAsync },
            new CommandInfo { Name = "wifi", Description = "Show the gateway's WiFi state.", Run = WifiStateAsync },
            new CommandInfo { Name = "wifi-scan", Description = "Ask the gateway to scan for WiFi networks and list them.", Run = WifiScanAsync },
            new CommandInfo { Name = "wifi-config-read", Description = "Read SSID and passphrase from the gateway's web page (clear-text HTTP).", NeedsGateway = false, NeedsLogin = false, Run = WifiConfigReadAsync },
            new CommandInfo { Name = "probe", Description = "Send the read commands whose answers the app never uses and print the raw replies.", NeedsLogin = false, Run = ProbeAsync },

            new CommandInfo { Name = "press", Arguments = "<portId>", Description = "Trigger a port (SET_STATE). This moves the drive.", Describe = o => $"trigger port {o.IntArgument(0, "portId")} - the drive will MOVE", Run = PressAsync },
            new CommandInfo { Name = "press-watch", Arguments = "<groupId> <portId>", Description = "Trigger a port and follow the group until settled.", Describe = o => $"trigger port {o.IntArgument(1, "portId")} of group {o.IntArgument(0, "groupId")} - the drive will MOVE", Run = PressWatchAsync },
            new CommandInfo { Name = "set-name", Arguments = "<name>", Description = "Rename the gateway.", Describe = o => $"rename the gateway to '{o.Argument(0, "name")}'", Run = s => s.Client.SetNameAsync(s.Options.Argument(0, "name")) },
            new CommandInfo { Name = "add-user", Arguments = "<name> [password]", Description = "Create a user.", Describe = o => $"create user '{o.Argument(0, "name")}'", Run = AddUserAsync },
            new CommandInfo { Name = "remove-user", Arguments = "<userId>", Description = "Delete a user.", Describe = o => $"DELETE user {o.IntArgument(0, "userId")}", Run = s => s.Client.RemoveUserAsync(s.Options.IntArgument(0, "userId")) },
            new CommandInfo { Name = "set-rights", Arguments = "<userId> <groupIds|none>", Description = "Replace the groups a user may operate (comma separated).", Describe = o => $"set the rights of user {o.IntArgument(0, "userId")} to groups [{o.Argument(1, "groupIds")}]", Run = s => s.Client.SetUserRightsAsync(s.Options.IntArgument(0, "userId"), ParseIds(s.Options.Argument(1, "groupIds"))) },
            new CommandInfo { Name = "rename-user", Arguments = "<userId> <name>", Description = "Rename a user.", Describe = o => $"rename user {o.IntArgument(0, "userId")} to '{o.Argument(1, "name")}'", Run = s => s.Client.ChangeUserNameAsync(s.Options.IntArgument(0, "userId"), s.Options.Argument(1, "name")) },
            new CommandInfo { Name = "set-password", Arguments = "<userId> [password]", Description = "Change a user's password.", Describe = o => $"change the password of user {o.IntArgument(0, "userId")}", Run = SetPasswordAsync },
            new CommandInfo { Name = "create-group", Arguments = "<name> <groupType>", Description = "Create a group.", Describe = o => $"create group '{o.Argument(0, "name")}' of type {ParseEnum<GroupType>(o.Argument(1, "groupType"))}", Run = CreateGroupAsync },
            new CommandInfo { Name = "rename-group", Arguments = "<groupId> <name>", Description = "Rename a group.", Describe = o => $"rename group {o.IntArgument(0, "groupId")} to '{o.Argument(1, "name")}'", Run = s => s.Client.RenameGroupAsync(s.Options.IntArgument(0, "groupId"), s.Options.Argument(1, "name")) },
            new CommandInfo { Name = "set-group-type", Arguments = "<groupId> <groupType>", Description = "Change a group's type.", Describe = o => $"set the type of group {o.IntArgument(0, "groupId")} to {ParseEnum<GroupType>(o.Argument(1, "groupType"))}", Run = s => s.Client.SetGroupTypeAsync(s.Options.IntArgument(0, "groupId"), ParseEnum<GroupType>(s.Options.Argument(1, "groupType"))) },
            new CommandInfo { Name = "set-requestable-port", Arguments = "<groupId> <portId|none>", Description = "Choose the port used to ask for a group's state.", Describe = o => $"set the requestable port of group {o.IntArgument(0, "groupId")} to {o.Argument(1, "portId")}", Run = s => s.Client.SetRequestablePortAsync(s.Options.IntArgument(0, "groupId"), ParseOptionalId(s.Options.Argument(1, "portId"))) },
            new CommandInfo { Name = "set-grouped-ports", Arguments = "<groupId> <portIds|none>", Description = "Replace the ports of a group (comma separated).", Describe = o => $"set the ports of group {o.IntArgument(0, "groupId")} to [{o.Argument(1, "portIds")}]", Run = s => s.Client.SetGroupedPortsAsync(s.Options.IntArgument(0, "groupId"), ParseIds(s.Options.Argument(1, "portIds"))) },
            new CommandInfo { Name = "delete-group", Arguments = "<groupId>", Description = "Delete a group and its ports.", Describe = o => $"DELETE group {o.IntArgument(0, "groupId")} and all its ports", Run = DeleteGroupAsync },
            new CommandInfo { Name = "add-port", Arguments = "<groupId> <portType> [--learn]", Description = "Add a port to a group: copied from a hand transmitter, or learned from the drive with --learn.", Describe = o => $"{(o.Learn ? "learn" : "copy")} a new {ParseEnum<PortType>(o.Argument(1, "portType"))} port into group {o.IntArgument(0, "groupId")} (the gateway waits up to 25 s for the transmitter or drive)", Run = AddPortAsync },
            new CommandInfo { Name = "remove-port", Arguments = "<portId>", Description = "Delete a port.", Describe = o => $"DELETE port {o.IntArgument(0, "portId")}", Run = s => s.Client.RemovePortAsync(s.Options.IntArgument(0, "portId")) },
            new CommandInfo { Name = "set-value", Arguments = "<key> <value>", Description = "Write one entry of the key-value store.", Describe = o => $"write value {o.IntArgument(1, "value")} to key {o.IntArgument(0, "key")}", Run = s => s.Client.SetValueAsync(s.Options.IntArgument(0, "key"), s.Options.IntArgument(1, "value")) },
            new CommandInfo { Name = "wifi-config-write", Arguments = "<ssid> [passphrase]", Description = "Store WiFi credentials through the gateway's web page (clear-text HTTP).", NeedsGateway = false, NeedsLogin = false, Describe = o => $"store WiFi credentials for network '{o.Argument(0, "ssid")}' over unencrypted HTTP", Run = WifiConfigWriteAsync },
            new CommandInfo { Name = "raw", Arguments = "<commandCode> [payloadHex]", Description = "Send any command code with a raw payload and print the reply.", Describe = o => $"send raw command {Command.GetCommand(o.IntArgument(0, "commandCode")).Name} ({o.IntArgument(0, "commandCode")}) with payload '{(o.Arguments.Count > 1 ? o.Arguments[1] : "")}'", Run = RawAsync },
        };

        internal static async Task<int> Main(string[] args)
        {
            Options options;
            try
            {
                options = Options.Parse(args);
            }
            catch (ArgumentException e)
            {
                Console.Error.WriteLine(e.Message);
                return ExitUsage;
            }

            if (options.Command == null || options.Command == "help")
            {
                PrintUsage();
                return ExitOk;
            }

            CommandInfo command = Commands.FirstOrDefault(c => c.Name == options.Command);
            if (command == null)
            {
                Console.Error.WriteLine($"Unknown command '{options.Command}'. Run 'help' for the list.");
                return ExitUsage;
            }

            try
            {
                return await RunAsync(command, options).ConfigureAwait(false);
            }
            catch (ArgumentException e)
            {
                Console.Error.WriteLine(e.Message);
                Console.Error.WriteLine($"Usage: {command.Name} {command.Arguments}");
                return ExitUsage;
            }
            catch (GatewayException e)
            {
                Console.Error.WriteLine($"Gateway error: {e.Error} ({(int)e.Error}) in answer to {e.Command?.Name}.");
                return ExitFailed;
            }
            catch (Exception e) when (e is TimeoutException || e is IOException || e is SocketException || e is HttpRequestException
                || e is GatewayProtocolException || e is InvalidOperationException || e is NotSupportedException || e is UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"{e.GetType().Name}: {e.Message}");
                return ExitFailed;
            }
        }

        private static async Task<int> RunAsync(CommandInfo command, Options options)
        {
            if (command.Writes)
            {
                // Evaluated first so that bad arguments are reported before anything is sent.
                string action = command.Describe(options);

                if (!options.Write)
                {
                    Console.Error.WriteLine($"'{command.Name}' changes the gateway ({action}). Add --write to allow it.");
                    return ExitUsage;
                }

                if (!Confirm(options, action))
                {
                    Console.Error.WriteLine("Aborted; nothing was sent.");
                    return ExitAborted;
                }
            }

            Session session = new Session
            {
                Options = options,
                Logger = new ConsoleLogger(options.Trace ? LogLevel.Trace : LogLevel.Warning),
            };

            if (options.Trace)
                Console.Error.WriteLine("Note: --trace prints every package, including the login package that contains the password.");

            if (!command.NeedsGateway)
            {
                await command.Run(session).ConfigureAwait(false);
                return ExitOk;
            }

            using (GatewayClient client = await ConnectAsync(session).ConfigureAwait(false))
            {
                session.Client = client;

                if (command.NeedsLogin)
                    await LoginAsync(session).ConfigureAwait(false);

                try
                {
                    await command.Run(session).ConfigureAwait(false);

                    if (command.Writes)
                        Console.WriteLine("Done.");
                }
                finally
                {
                    await LogoutAsync(session).ConfigureAwait(false);
                }
            }

            return ExitOk;
        }

        private static bool Confirm(Options options, string action)
        {
            Console.Error.WriteLine($"About to {action} on gateway {options.Host ?? "(not set)"}.");
            if (options.Yes)
                return true;

            Console.Error.Write("Type 'yes' to continue: ");

            return string.Equals(Console.ReadLine()?.Trim(), "yes", StringComparison.OrdinalIgnoreCase);
        }

        private static async Task<GatewayClient> ConnectAsync(Session session)
        {
            Options options = session.Options;
            IPAddress address = ParseHost(options);

            string mac = options.Mac;
            if (mac == null)
            {
                // The protocol addresses the gateway by its MAC, which only discovery reports.
                IReadOnlyList<DiscoveryResult> found = await new GatewayDiscovery(session.Logger).DiscoverAsync(TimeSpan.FromSeconds(options.TimeoutSeconds)).ConfigureAwait(false);
                DiscoveryResult match = found.FirstOrDefault(g => address.Equals(g.SourceAddress));
                if (match == null)
                    throw new ArgumentException($"Gateway {address} did not answer discovery, so its MAC is unknown. Pass --mac.");

                mac = match.GatewayId;
            }

            mac = mac.Replace(":", "").Replace("-", "").ToUpperInvariant();
            if (mac.Length != 12 || !mac.All(Uri.IsHexDigit))
                throw new ArgumentException($"'{options.Mac}' is not a MAC address (12 hex digits).");

            GatewayConnection connection = new GatewayConnection(address, null, mac, options.Port);
            connection.SetLogger(session.Logger);
            await connection.ConnectAsync().ConfigureAwait(false);

            GatewayClient client = new GatewayClient(connection);
            client.SetLogger(session.Logger);

            return client;
        }

        private static async Task LoginAsync(Session session)
        {
            Options options = session.Options;

            string user = options.User;
            if (user == null)
            {
                Console.Error.Write("User: ");
                user = Console.ReadLine();
            }

            string password = options.Password ?? ReadSecret("Password: ");

            LoginResult result = await session.Client.LoginAsync(user, password).ConfigureAwait(false);
            Console.Error.WriteLine($"Logged in as '{user}' (user id {result.UserId}{(result.IsAdmin ? ", administrator" : "")}).");
        }

        private static async Task LogoutAsync(Session session)
        {
            GatewayClient client = session.Client;
            if (!client.Connection.Connected || !client.Connection.IsAuthenticated)
                return;

            // Whether the gateway answers LOGOUT is not known from the app, so do not wait long.
            client.Connection.Timeouts.Set(Command.Logout, 2000);
            try
            {
                Message response = await client.SendRawAsync(Command.Logout).ConfigureAwait(false);
                Console.Error.WriteLine($"Logged out (gateway answered {response.Command.Name}).");
            }
            catch (TimeoutException)
            {
                Console.Error.WriteLine("Sent LOGOUT; the gateway did not answer it within 2 s.");
            }
            catch (Exception e) when (e is IOException || e is InvalidOperationException)
            {
                Console.Error.WriteLine($"Logout was not sent: {e.Message}");
            }
        }

        private static async Task DiscoverAsync(Session session)
        {
            GatewayDiscovery discovery = new GatewayDiscovery(session.Logger);
            IReadOnlyList<DiscoveryResult> gateways = await discovery.DiscoverAsync(TimeSpan.FromSeconds(session.Options.TimeoutSeconds)).ConfigureAwait(false);

            if (gateways.Count == 0)
            {
                Console.WriteLine("No gateway answered.");
                return;
            }

            await DiscoveryNameResolver.ResolveNamesAsync(gateways, session.Logger).ConfigureAwait(false);

            foreach (DiscoveryResult gateway in gateways)
                Console.WriteLine($"{gateway.SourceAddress}  mac {gateway.GatewayId}  name '{gateway.Name ?? "(no answer)"}'  protocol {gateway.Protocol}  hw {gateway.HwVersion}  sw {gateway.SwVersion}");
        }

        private static async Task InfoAsync(Session session)
        {
            GatewayClient client = session.Client;

            await client.PingAsync().ConfigureAwait(false);
            Console.WriteLine("Ping:      answered");
            Console.WriteLine($"Name:      {await client.GetNameAsync().ConfigureAwait(false)}");

            GatewayVersion version = await client.GetFirmwareVersionAsync().ConfigureAwait(false);
            Console.WriteLine($"Firmware:  {version.Name} (number {version.Number})");
            Console.WriteLine($"Client id: {client.Connection.ClientId}");
        }

        private static async Task UsersAsync(Session session)
        {
            GatewayClient client = session.Client;

            foreach (User user in await client.GetUsersAsync().ConfigureAwait(false))
            {
                string rights;
                try
                {
                    rights = string.Join(",", await client.GetUserRightsAsync(user.Id).ConfigureAwait(false));
                }
                catch (GatewayException e)
                {
                    rights = $"(error {e.Error})";
                }

                Console.WriteLine($"user {user.Id,2}  '{user.Name}'  admin {user.IsAdmin}  groups from list [{string.Join(",", user.Groups ?? new int[0])}]  rights [{rights}]");
            }
        }

        private static async Task GroupsAsync(Session session)
        {
            foreach (Group group in await session.Client.GetGroupsAsync().ConfigureAwait(false))
            {
                string ports = string.Join(", ", group.Ports.Select(p => $"{p.Id}:{p.Type}"));
                string requestable = group.RequestablePortId?.ToString() ?? "none";
                Console.WriteLine($"group {group.Id,2}  '{group.Name}'  type {group.Type}  requestable port {requestable}  ports [{ports}]");
            }

            Console.WriteLine($"Ports stored on the gateway: {await session.Client.GetPortCountAsync().ConfigureAwait(false)} of {GatewayLimits.MaxPorts}");
        }

        private static async Task ValuesAsync(Session session)
        {
            IReadOnlyDictionary<int, int> values = await session.Client.GetValuesAsync(true).ConfigureAwait(false);

            foreach (KeyValuePair<int, int> pair in values.OrderBy(p => p.Key))
            {
                string meaning = pair.Key < GatewayLimits.MaxPorts ? $"type of group {pair.Key}"
                    : pair.Key < 2 * GatewayLimits.MaxPorts ? $"requestable port of group {pair.Key - GatewayLimits.MaxPorts}"
                    : "not used by the app";
                Console.WriteLine($"{pair.Key,2} = {pair.Value,3}   {meaning}");
            }
        }

        private static async Task StateAsync(Session session)
        {
            Group[] groups = await session.Client.GetGroupsAsync().ConfigureAwait(false);
            if (session.Options.Arguments.Count > 0)
                groups = new[] { FindGroup(groups, session.Options.IntArgument(0, "groupId")) };

            foreach (Group group in groups)
            {
                Console.WriteLine($"group {group.Id} '{group.Name}' ({group.Type})");
                if (group.RequestablePortId == null)
                {
                    Console.WriteLine("  cannot report its state (no requestable port)");
                    continue;
                }

                try
                {
                    Transition transition = await session.Client.GetTransitionAsync(group).ConfigureAwait(false);
                    StateOutput.Print(transition, group);
                }
                catch (Exception e) when (e is GatewayException || e is GatewayProtocolException || e is TimeoutException)
                {
                    Console.WriteLine($"  failed: {e.Message}");
                }
            }
        }

        private static async Task WatchAsync(Session session)
        {
            Group group = FindGroup(await session.Client.GetGroupsAsync().ConfigureAwait(false), session.Options.IntArgument(0, "groupId"));

            using (CancellationTokenSource cancellation = CancelOnCtrlC())
            {
                TransitionMonitor monitor = new TransitionMonitor(session.Client);
                try
                {
                    await monitor.FollowAsync(group, t => StateOutput.Print(t, group), cancellation.Token).ConfigureAwait(false);
                    Console.WriteLine("Settled.");
                }
                catch (OperationCanceledException)
                {
                    Console.WriteLine("Stopped.");
                }
            }
        }

        private static async Task WifiStateAsync(Session session)
        {
            WifiState state = await session.Client.GetWifiStateAsync().ConfigureAwait(false);
            Console.WriteLine($"WiFi state: {state} ({(byte)state})");
        }

        private static async Task WifiScanAsync(Session session)
        {
            IReadOnlyList<string> networks = await session.Client.ScanWifiAsync().ConfigureAwait(false);
            Console.WriteLine($"{networks.Count} network(s) reported:");
            foreach (string network in networks)
                Console.WriteLine($"  {network}");
        }

        private static async Task WifiConfigReadAsync(Session session)
        {
            GatewayWebConfiguration web = new GatewayWebConfiguration(ParseHost(session.Options));
            GatewayWifiSettings settings = await web.ReadWifiSettingsAsync().ConfigureAwait(false);

            Console.WriteLine($"SSID:       {settings.Ssid ?? "(not found on the page)"}");
            Console.WriteLine($"Passphrase: {(settings.Passphrase == null ? "(not found on the page)" : $"{settings.Passphrase.Length} characters (not shown)")}");
        }

        private static async Task WifiConfigWriteAsync(Session session)
        {
            string passphrase = session.Options.Arguments.Count > 1 ? session.Options.Arguments[1] : ReadSecret("WiFi passphrase: ");

            GatewayWebConfiguration web = new GatewayWebConfiguration(ParseHost(session.Options));
            await web.WriteWifiSettingsAsync(session.Options.Argument(0, "ssid"), passphrase).ConfigureAwait(false);
            Console.WriteLine("Done.");
        }

        private static IPAddress ParseHost(Options options)
        {
            if (options.Host == null)
                throw new ArgumentException("No gateway given. Use --host <ip> [--mac <mac>] or set BISECUR_HOST / BISECUR_MAC. Run 'discover' to find gateways.");

            if (!IPAddress.TryParse(options.Host, out IPAddress address))
                throw new ArgumentException($"'{options.Host}' is not an IP address.");

            return address;
        }

        // Settles what the app's code cannot: these commands have request builders but their answers are never read.
        private static async Task ProbeAsync(Session session)
        {
            GatewayClient client = session.Client;

            Console.WriteLine("Before login:");
            await ProbeOneAsync(client, Command.GetMac, null).ConfigureAwait(false);
            await ProbeOneAsync(client, Command.GetGatewayVersion, null).ConfigureAwait(false);

            if (session.Options.User == null)
            {
                Console.WriteLine("No --user given; skipping the probes that need a login.");
                return;
            }

            await LoginAsync(session).ConfigureAwait(false);

            Console.WriteLine("After login:");
            await ProbeOneAsync(client, Command.GetValue, PayloadFactory.GetValue(0)).ConfigureAwait(false);
            Message ports = await ProbeOneAsync(client, Command.GetPorts, null).ConfigureAwait(false);

            byte[] portBytes = ports?.Payload.ToByteArray() ?? new byte[0];
            if (ports != null && !ports.IsError && portBytes.Length > 0)
                await ProbeOneAsync(client, Command.GetPortType, PayloadFactory.GetPortType(portBytes[0])).ConfigureAwait(false);
        }

        private static async Task<Message> ProbeOneAsync(GatewayClient client, Command command, Payload payload)
        {
            try
            {
                Message response = await client.SendRawAsync(command, payload).ConfigureAwait(false);
                PrintRaw(command, payload, response);
                return response;
            }
            catch (Exception e) when (e is TimeoutException || e is InvalidOperationException)
            {
                Console.WriteLine($"  {command.Name}: {e.GetType().Name}: {e.Message}");
                return null;
            }
        }

        private static async Task PressAsync(Session session)
        {
            Transition transition = await session.Client.SetStateAsync(session.Options.IntArgument(0, "portId")).ConfigureAwait(false);

            if (transition == null)
                Console.WriteLine("The gateway acknowledged the command without a state.");
            else
                StateOutput.Print(transition, null);
        }

        private static async Task PressWatchAsync(Session session)
        {
            Group group = FindGroup(await session.Client.GetGroupsAsync().ConfigureAwait(false), session.Options.IntArgument(0, "groupId"));
            int portId = session.Options.IntArgument(1, "portId");
            Port port = group.Ports.FirstOrDefault(p => p.Id == portId)
                ?? throw new ArgumentException($"Group {group.Id} has no port {portId}.");

            using (CancellationTokenSource cancellation = CancelOnCtrlC())
            {
                TransitionMonitor monitor = new TransitionMonitor(session.Client);
                try
                {
                    await monitor.SetStateAndFollowAsync(group, port, t => StateOutput.Print(t, group), cancellation.Token).ConfigureAwait(false);
                    Console.WriteLine("Settled.");
                }
                catch (OperationCanceledException)
                {
                    Console.WriteLine("Stopped watching (the drive keeps moving).");
                }
            }
        }

        private static async Task AddUserAsync(Session session)
        {
            string password = session.Options.Arguments.Count > 1 ? session.Options.Arguments[1] : ReadSecret("Password for the new user: ");
            WarnAboutPassword(password);

            int userId = await session.Client.AddUserAsync(session.Options.Argument(0, "name"), password).ConfigureAwait(false);
            Console.WriteLine($"Created user {userId}.");
        }

        private static async Task SetPasswordAsync(Session session)
        {
            string password = session.Options.Arguments.Count > 1 ? session.Options.Arguments[1] : ReadSecret("New password: ");
            WarnAboutPassword(password);

            await session.Client.ChangePasswordAsync(session.Options.IntArgument(0, "userId"), password).ConfigureAwait(false);
        }

        private static async Task CreateGroupAsync(Session session)
        {
            Group group = await session.Client.CreateGroupAsync(session.Options.Argument(0, "name"), ParseEnum<GroupType>(session.Options.Argument(1, "groupType"))).ConfigureAwait(false);
            Console.WriteLine($"Created group {group.Id}.");
        }

        private static async Task DeleteGroupAsync(Session session)
        {
            Group group = FindGroup(await session.Client.GetGroupsAsync().ConfigureAwait(false), session.Options.IntArgument(0, "groupId"));
            await session.Client.DeleteGroupAsync(group).ConfigureAwait(false);
        }

        private static async Task AddPortAsync(Session session)
        {
            Group group = FindGroup(await session.Client.GetGroupsAsync().ConfigureAwait(false), session.Options.IntArgument(0, "groupId"));
            PortType type = ParseEnum<PortType>(session.Options.Argument(1, "portType"));

            Console.WriteLine(session.Options.Learn ? "Waiting for the drive..." : "Press the button on the hand transmitter now...");
            Port port = await session.Client.AddPortToGroupAsync(group, type, session.Options.Learn).ConfigureAwait(false);
            Console.WriteLine($"Created port {port.Id} ({port.Type}) in group {group.Id}.");
        }

        private static async Task RawAsync(Session session)
        {
            Command command = Command.GetCommand(session.Options.IntArgument(0, "commandCode"));
            Payload payload = session.Options.Arguments.Count > 1
                ? new Payload(StringHelper.HexStringToByteArray(session.Options.Arguments[1]))
                : PayloadFactory.Empty;

            Message response = await session.Client.SendRawAsync(command, payload).ConfigureAwait(false);
            PrintRaw(command, payload, response);
        }

        private static void PrintRaw(Command command, Payload payload, Message response)
        {
            byte[] data = response.Payload.ToByteArray();
            string request = payload == null ? "" : $" [{StringHelper.HexStringFromByteArray(payload.ToByteArray(), " ").Trim()}]";
            string error = response.IsError ? $" = {response.GetError()}" : "";

            Console.WriteLine($"  {command.Name}{request} -> {response.Command.Name}{error}, {data.Length} payload byte(s)");
            if (data.Length > 0)
            {
                Console.WriteLine($"    hex:  {StringHelper.HexStringFromByteArray(data, " ").Trim()}");
                Console.WriteLine($"    text: {new string(Encoding.ASCII.GetString(data).Select(c => char.IsControl(c) ? '.' : c).ToArray())}");
            }
        }

        private static void WarnAboutPassword(string password)
        {
            PasswordIssues issues = GatewayValidation.CheckPassword(password);
            if (issues != PasswordIssues.None)
                Console.Error.WriteLine($"Note: the app would flag this password as weak ({issues}).");
        }

        private static Group FindGroup(Group[] groups, int groupId)
        {
            return groups.FirstOrDefault(g => g.Id == groupId)
                ?? throw new ArgumentException($"The gateway has no group {groupId} visible to this user.");
        }

        private static int[] ParseIds(string text)
        {
            if (string.Equals(text, "none", StringComparison.OrdinalIgnoreCase))
                return new int[0];

            return text.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(part =>
            {
                if (!int.TryParse(part.Trim(), out int id))
                    throw new ArgumentException($"'{part}' is not a number.");

                return id;
            }).ToArray();
        }

        private static int? ParseOptionalId(string text)
        {
            if (string.Equals(text, "none", StringComparison.OrdinalIgnoreCase))
                return null;

            if (!int.TryParse(text, out int id))
                throw new ArgumentException($"'{text}' is neither a number nor 'none'.");

            return id;
        }

        private static T ParseEnum<T>(string text) where T : struct, Enum
        {
            if (Enum.TryParse(text, true, out T value) && Enum.IsDefined(value))
                return value;

            throw new ArgumentException($"'{text}' is not a {typeof(T).Name}. Valid: {string.Join(", ", Enum.GetNames<T>())}.");
        }

        private static string ReadSecret(string prompt)
        {
            Console.Error.Write(prompt);

            if (Console.IsInputRedirected)
                return Console.ReadLine() ?? "";

            StringBuilder secret = new StringBuilder();
            while (true)
            {
                ConsoleKeyInfo key = Console.ReadKey(true);
                if (key.Key == ConsoleKey.Enter)
                    break;

                if (key.Key == ConsoleKey.Backspace)
                {
                    if (secret.Length > 0)
                        secret.Length--;
                }
                else if (!char.IsControl(key.KeyChar))
                {
                    secret.Append(key.KeyChar);
                }
            }

            Console.Error.WriteLine();

            return secret.ToString();
        }

        private static CancellationTokenSource CancelOnCtrlC()
        {
            CancellationTokenSource cancellation = new CancellationTokenSource();
            Console.CancelKeyPress += (sender, e) =>
            {
                e.Cancel = true;
                cancellation.Cancel();
            };

            return cancellation;
        }

        private static void PrintUsage()
        {
            Console.WriteLine("bisec-harness - manual checks against a real BiSecur gateway");
            Console.WriteLine();
            Console.WriteLine("Usage: bisec-harness <command> [arguments] [options]");
            Console.WriteLine();
            Console.WriteLine("Options:");
            Console.WriteLine("  --host <ip>         gateway address            (or BISECUR_HOST)");
            Console.WriteLine("  --mac <mac>         gateway MAC                (or BISECUR_MAC; found by discovery when omitted)");
            Console.WriteLine("  --user <name>       user name                  (or BISECUR_USER; asked for when omitted)");
            Console.WriteLine("  --password <text>   password                   (or BISECUR_PASSWORD; asked for, hidden, when omitted)");
            Console.WriteLine("  --port <number>     TCP port, default 4000");
            Console.WriteLine("  --timeout <s>       discovery window in seconds, default 3");
            Console.WriteLine("  --write             allow a command that changes the gateway");
            Console.WriteLine("  --yes               skip the typed confirmation of a --write command");
            Console.WriteLine("  --trace             print every package sent and received (includes the password at login)");
            Console.WriteLine();
            Console.WriteLine("Nothing is stored. A password given with --password is visible to other processes; prefer the prompt.");

            PrintCommands("Read-only commands:", Commands.Where(c => !c.Writes));
            PrintCommands("Commands that change the gateway (need --write):", Commands.Where(c => c.Writes));
        }

        private static void PrintCommands(string title, IEnumerable<CommandInfo> commands)
        {
            Console.WriteLine();
            Console.WriteLine(title);
            foreach (CommandInfo command in commands)
                Console.WriteLine($"  {(command.Name + " " + command.Arguments).TrimEnd(),-42} {command.Description}");
        }
    }
}
