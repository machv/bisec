using BiSec.Library.Exceptions;
using BiSec.Library.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BiSec.Library
{
    public partial class GatewayClient
    {
        /// <summary>
        /// Returns the gateway's 64-entry key-value store. Group types live at key groupId and
        /// requestable ports at key groupId + 16 (see <see cref="GatewayValues"/>).
        /// </summary>
        public async Task<IReadOnlyDictionary<int, int>> GetValuesAsync(bool refresh = false, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (_values != null && !refresh)
                return _values;

            Message response = await RequestAsync(Command.Jmcp, PayloadFactory.GetValues(), cancellationToken).ConfigureAwait(false);
            _values = ParseJsonResponse<Dictionary<int, int>>(response.Payload.TextContent) ?? new Dictionary<int, int>();

            return _values;
        }

        public async Task SetValueAsync(int key, int value, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (key < 0 || key >= GatewayLimits.MaxValues)
                throw new ArgumentOutOfRangeException(nameof(key), key, $"A value key must be between 0 and {GatewayLimits.MaxValues - 1}.");

            await RequestAsync(Command.SetValue, PayloadFactory.SetValue(key, value), cancellationToken).ConfigureAwait(false);

            if (_values != null)
                _values[key] = value;
        }

        /// <summary>
        /// Returns the groups visible to the logged-in user, with type and requestable port filled in.
        /// </summary>
        public Task<Group[]> GetGroupsAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            // The app asks for all groups as administrator and for the user's own groups otherwise.
            Payload payload = _userId.HasValue && !IsAdmin
                ? PayloadFactory.GetGroupsForUser(_userId.Value)
                : PayloadFactory.GetGroups();

            return LoadGroupsAsync(payload, cancellationToken);
        }

        public Task<Group[]> GetGroupsForUserAsync(int userId, CancellationToken cancellationToken = default(CancellationToken))
        {
            return LoadGroupsAsync(PayloadFactory.GetGroupsForUser(userId), cancellationToken);
        }

        private async Task<Group[]> LoadGroupsAsync(Payload payload, CancellationToken cancellationToken)
        {
            Message response = await RequestAsync(Command.Jmcp, payload, cancellationToken).ConfigureAwait(false);
            Group[] groups = ParseJsonResponse<Group[]>(response.Payload.TextContent) ?? new Group[0];

            IReadOnlyDictionary<int, int> values = await GetValuesAsync(true, cancellationToken).ConfigureAwait(false);

            foreach (Group group in groups)
            {
                if (group.Ports == null)
                    group.Ports = new Port[0];

                if (group.Id < 0 || group.Id >= GatewayLimits.MaxPorts)
                    continue;

                if (values.TryGetValue(GatewayValues.GroupTypeKey(group.Id), out int type))
                    group.Type = (GroupType)type;

                if (values.TryGetValue(GatewayValues.RequestablePortKey(group.Id), out int portId) && portId != GatewayLimits.NoPort)
                    group.RequestablePortId = portId;
            }

            return groups;
        }

        /// <summary>
        /// Creates a group, names it and stores its type. The gateway does this in three requests;
        /// when a later one fails, the group already exists.
        /// </summary>
        public async Task<Group> CreateGroupAsync(string name, GroupType type, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (name == null)
                throw new ArgumentNullException(nameof(name));

            Message response = await RequestAsync(Command.AddGroup, PayloadFactory.Empty, cancellationToken).ConfigureAwait(false);
            int groupId = RequirePayload(response, Command.AddGroup, 1)[0];

            await RenameGroupAsync(groupId, name, cancellationToken).ConfigureAwait(false);
            await SetGroupTypeAsync(groupId, type, cancellationToken).ConfigureAwait(false);

            return new Group
            {
                Id = groupId,
                Name = name,
                Type = type,
                Ports = new Port[0],
            };
        }

        public async Task RenameGroupAsync(int groupId, string name, CancellationToken cancellationToken = default(CancellationToken))
        {
            await RequestAsync(Command.SetGroupName, PayloadFactory.SetGroupName(groupId, name), cancellationToken).ConfigureAwait(false);
        }

        public Task SetGroupTypeAsync(int groupId, GroupType type, CancellationToken cancellationToken = default(CancellationToken))
        {
            return SetValueAsync(GatewayValues.GroupTypeKey(groupId), (int)type, cancellationToken);
        }

        /// <summary>
        /// Sets the port the gateway is asked for the group's state; null marks the group as unable to report it.
        /// </summary>
        public Task SetRequestablePortAsync(int groupId, int? portId, CancellationToken cancellationToken = default(CancellationToken))
        {
            return SetValueAsync(GatewayValues.RequestablePortKey(groupId), portId ?? GatewayLimits.NoPort, cancellationToken);
        }

        /// <summary>
        /// Replaces the list of ports that belong to the group.
        /// </summary>
        public async Task SetGroupedPortsAsync(int groupId, IEnumerable<int> portIds, CancellationToken cancellationToken = default(CancellationToken))
        {
            await RequestAsync(Command.SetGroupedPorts, PayloadFactory.SetGroupedPorts(groupId, portIds), cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Removes the group's ports and then the group, as the app does. A port the gateway no
        /// longer knows is skipped.
        /// </summary>
        public async Task DeleteGroupAsync(Group group, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (group == null)
                throw new ArgumentNullException(nameof(group));

            foreach (Port port in group.Ports ?? new Port[0])
            {
                try
                {
                    await RemovePortAsync(port.Id, cancellationToken).ConfigureAwait(false);
                }
                catch (GatewayException e) when (e.Error == Error.PortNotFound)
                {
                    _logger?.LogInformation($"Port {port.Id} of group {group.Id} was already gone.");
                }
            }

            await RequestAsync(Command.RemoveGroup, PayloadFactory.RemoveGroup(group.Id), cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Number of ports stored on the gateway (at most <see cref="GatewayLimits.MaxPorts"/>).
        /// </summary>
        public async Task<int> GetPortCountAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            Message response = await RequestAsync(Command.GetPorts, PayloadFactory.Empty, cancellationToken).ConfigureAwait(false);

            // The app counts one port per payload byte and does not interpret the bytes.
            return response.Payload.ToByteArray().Length;
        }

        /// <summary>
        /// Creates a port by copying a channel from a hand transmitter (ADD_PORT) and sets its type.
        /// The gateway waits for the transmitter, so this can take up to 25 seconds.
        /// </summary>
        public Task<Port> AddPortAsync(PortType type, CancellationToken cancellationToken = default(CancellationToken))
        {
            return CreatePortAsync(Command.AddPort, type, cancellationToken);
        }

        /// <summary>
        /// Creates a port by learning it from the drive (INHERIT_PORT) and sets its type.
        /// </summary>
        public Task<Port> LearnPortAsync(PortType type, CancellationToken cancellationToken = default(CancellationToken))
        {
            return CreatePortAsync(Command.InheritPort, type, cancellationToken);
        }

        private async Task<Port> CreatePortAsync(Command command, PortType type, CancellationToken cancellationToken)
        {
            int portCount = await GetPortCountAsync(cancellationToken).ConfigureAwait(false);
            if (portCount >= GatewayLimits.MaxPorts)
                throw new InvalidOperationException($"The gateway already holds the maximum of {GatewayLimits.MaxPorts} ports.");

            Message response = await RequestAsync(command, PayloadFactory.Empty, cancellationToken).ConfigureAwait(false);
            int portId = RequirePayload(response, command, 1)[0];

            await SetPortTypeAsync(portId, type, cancellationToken).ConfigureAwait(false);

            return new Port
            {
                Id = portId,
                Type = type,
            };
        }

        /// <summary>
        /// Creates a port of the given type and appends it to the group.
        /// </summary>
        /// <param name="learn">True to learn the port from the drive, false to copy it from a hand transmitter.</param>
        public async Task<Port> AddPortToGroupAsync(Group group, PortType type, bool learn, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (group == null)
                throw new ArgumentNullException(nameof(group));

            // The app only offers learning from the drive for non-SmartKey groups (HmGroup.features).
            if (learn && !group.Type.HasFeature(GroupFeatures.InheritPort))
                throw new NotSupportedException($"Groups of type {group.Type} cannot learn a port from the drive.");

            IReadOnlyList<PortType> allowed = group.Type.GetAllowedPortTypes();
            if (allowed.Count > 0 && !allowed.Contains(type))
                throw new ArgumentException($"Groups of type {group.Type} have no {type} port. Allowed: {string.Join(", ", allowed)}.", nameof(type));

            Port port = learn
                ? await LearnPortAsync(type, cancellationToken).ConfigureAwait(false)
                : await AddPortAsync(type, cancellationToken).ConfigureAwait(false);

            Port[] ports = (group.Ports ?? new Port[0]).Concat(new[] { port }).ToArray();
            await SetGroupedPortsAsync(group.Id, ports.Select(p => p.Id), cancellationToken).ConfigureAwait(false);
            group.Ports = ports;

            return port;
        }

        public async Task SetPortTypeAsync(int portId, PortType type, CancellationToken cancellationToken = default(CancellationToken))
        {
            await RequestAsync(Command.SetPortType, PayloadFactory.SetPortType(portId, (int)type), cancellationToken).ConfigureAwait(false);
        }

        public async Task RemovePortAsync(int portId, CancellationToken cancellationToken = default(CancellationToken))
        {
            await RequestAsync(Command.RemovePort, PayloadFactory.RemovePort(portId), cancellationToken).ConfigureAwait(false);
        }
    }
}
