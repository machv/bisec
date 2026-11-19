using BiSec.Library;
using BiSec.Library.Exceptions;
using BiSec.Library.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace tests
{
    /// <summary>
    /// Groups, ports and the key-value store.
    /// </summary>
    public class GatewayClientGroupsTests
    {
        private static byte[] Bytes(byte first, string text)
        {
            return new[] { first }.Concat(Encoding.UTF8.GetBytes(text)).ToArray();
        }

        private static async Task<Group> LoadGroupAsync(GatewayTestSession session, string name)
        {
            Group[] groups = await session.Client.GetGroupsAsync();
            session.Gateway.ClearRequests();

            return groups.Single(g => g.Name == name);
        }

        [Fact]
        public async Task AdministratorAsksForAllGroups()
        {
            using var session = await GatewayTestSession.AsAdminAsync();

            Group[] groups = await session.Client.GetGroupsAsync();

            Assert.Equal(new[] { "{\"cmd\":\"GET_GROUPS\"}", "{\"cmd\":\"GET_VALUES\"}" }, session.Gateway.Requests.Select(r => r.Text));
            Assert.Equal(new[] { "Garage", "Gate" }, groups.Select(g => g.Name));
            Assert.Equal(new[] { 0, 1 }, groups.Select(g => g.Id));

            Port garagePort = Assert.Single(groups[0].Ports);
            Assert.Equal(0, garagePort.Id);
            Assert.Equal(PortType.Impuls, garagePort.Type);
            Assert.Equal(new[] { 1, 2 }, groups[1].Ports.Select(p => p.Id));
            Assert.Equal(new[] { PortType.Impuls, PortType.Up }, groups[1].Ports.Select(p => p.Type));
        }

        [Fact]
        public async Task OtherUserAsksForItsOwnGroupsWithItsOwnId()
        {
            using var session = await GatewayTestSession.AsUserAsync();

            Group[] groups = await session.Client.GetGroupsAsync();

            Assert.Equal("{\"cmd\":\"GET_GROUPS\", \"forUser\":3}", session.Gateway.Requests[0].Text);
            Assert.Equal("Gate", Assert.Single(groups).Name);
        }

        [Fact]
        public async Task GetGroupsForUserAsksWithTheGivenId()
        {
            using var session = await GatewayTestSession.AsAdminAsync();

            Group[] groups = await session.Client.GetGroupsForUserAsync(GatewayTestSession.UserId);

            Assert.Equal("{\"cmd\":\"GET_GROUPS\", \"forUser\":3}", session.Gateway.Requests[0].Text);
            Assert.Equal("Gate", Assert.Single(groups).Name);
        }

        [Fact]
        public async Task GroupTypeAndRequestablePortComeFromTheValueStore()
        {
            using var session = await GatewayTestSession.AsAdminAsync();
            session.Gateway.Values[0] = (int)GroupType.Door;
            session.Gateway.Values[16] = 0;
            session.Gateway.Values[1] = (int)GroupType.Smartkey;
            session.Gateway.Values[17] = 2;

            Group[] groups = await session.Client.GetGroupsAsync();

            Assert.Equal(GroupType.Door, groups[0].Type);
            Assert.Equal(0, groups[0].RequestablePortId);
            Assert.Equal(GroupType.Smartkey, groups[1].Type);
            Assert.Equal(2, groups[1].RequestablePortId);
        }

        [Fact]
        public async Task RequestablePort255MeansTheGroupHasNone()
        {
            using var session = await GatewayTestSession.AsAdminAsync();
            session.Gateway.Values[17] = 255;

            Group[] groups = await session.Client.GetGroupsAsync();

            Assert.Equal(0, groups[0].RequestablePortId);
            Assert.Null(groups[1].RequestablePortId);
            Assert.Equal(GroupType.SlidingGate, groups[1].Type);
        }

        [Fact]
        public async Task GroupWhoseKeysAreMissingFromTheValueStoreDoesNotThrow()
        {
            using var session = await GatewayTestSession.AsAdminAsync();
            session.Gateway.Values.Remove(1);
            session.Gateway.Values.Remove(17);

            Group[] groups = await session.Client.GetGroupsAsync();

            Assert.Equal(GroupType.SectionalDoor, groups[0].Type);
            Assert.Equal(GroupType.None, groups[1].Type);
            Assert.Null(groups[1].RequestablePortId);
        }

        [Fact]
        public async Task GroupsAreLoadedWithFreshValues()
        {
            using var session = await GatewayTestSession.AsAdminAsync();
            await session.Client.GetValuesAsync();
            session.Gateway.Values[0] = (int)GroupType.Light;

            Group[] groups = await session.Client.GetGroupsAsync();

            Assert.Equal(GroupType.Light, groups[0].Type);
        }

        [Fact]
        public async Task AllSixtyFourValuesAreParsed()
        {
            using var session = await GatewayTestSession.AsAdminAsync();
            for (int key = 0; key < 64; key++)
                session.Gateway.Values[key] = 100 + key;

            IReadOnlyDictionary<int, int> values = await session.Client.GetValuesAsync();

            Assert.Equal("{\"cmd\":\"GET_VALUES\"}", Assert.Single(session.Gateway.Requests).Text);
            Assert.Equal(Enumerable.Range(0, 64), values.Keys.OrderBy(k => k));
            Assert.All(values, v => Assert.Equal(100 + v.Key, v.Value));
        }

        [Fact]
        public async Task SetValueSendsKeyAndValueAndGetValuesReflectsIt()
        {
            using var session = await GatewayTestSession.AsAdminAsync();
            await session.Client.GetValuesAsync();

            await session.Client.SetValueAsync(40, 200);
            IReadOnlyDictionary<int, int> cached = await session.Client.GetValuesAsync();

            Assert.Equal(200, cached[40]);
            Assert.Equal(new[] { "JMCP", "SET_VALUE" }, session.Gateway.RequestNames);
            Assert.Equal(new byte[] { 40, 200 }, session.Gateway.Requests[1].Payload);

            IReadOnlyDictionary<int, int> fresh = await session.Client.GetValuesAsync(refresh: true);

            Assert.Equal(200, fresh[40]);
            Assert.Equal(3, session.Gateway.Requests.Count);
        }

        [Fact]
        public async Task ValueSetBeforeTheFirstReadIsReadFromTheGateway()
        {
            using var session = await GatewayTestSession.AsAdminAsync();

            await session.Client.SetValueAsync(41, 7);
            IReadOnlyDictionary<int, int> values = await session.Client.GetValuesAsync();

            Assert.Equal(7, values[41]);
            Assert.Equal(64, values.Count);
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(64)]
        public async Task SetValueRejectsAKeyOutsideTheStoreWithoutSending(int key)
        {
            using var session = await GatewayTestSession.AsAdminAsync();

            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => session.Client.SetValueAsync(key, 1));
            await session.Client.PingAsync();

            Assert.Equal(new[] { "PING" }, session.Gateway.RequestNames);
        }

        [Fact]
        public async Task CreateGroupSendsAddGroupThenTheNameThenTheType()
        {
            using var session = await GatewayTestSession.AsAdminAsync();

            Group group = await session.Client.CreateGroupAsync("Carport", GroupType.Door);

            // Ids 0 and 1 are taken, so the fake hands out 2.
            Assert.Equal(new[] { "ADD_GROUP", "SET_GROUP_NAME", "SET_VALUE" }, session.Gateway.RequestNames);
            Assert.Empty(session.Gateway.Requests[0].Payload);
            Assert.Equal(Bytes(2, "Carport"), session.Gateway.Requests[1].Payload);
            Assert.Equal(new byte[] { 2, (byte)GroupType.Door }, session.Gateway.Requests[2].Payload);

            Assert.Equal(2, group.Id);
            Assert.Equal("Carport", group.Name);
            Assert.Equal(GroupType.Door, group.Type);
            Assert.Empty(group.Ports);

            Assert.Equal("Carport", session.Gateway.Groups.Single(g => g.Id == 2).Name);
            Assert.Equal((int)GroupType.Door, session.Gateway.Values[2]);
        }

        [Fact]
        public async Task SetRequestablePortStoresThePortOr255AtGroupIdPlus16()
        {
            using var session = await GatewayTestSession.AsAdminAsync();

            await session.Client.SetRequestablePortAsync(1, 2);
            await session.Client.SetRequestablePortAsync(1, null);

            Assert.Equal(new[] { "SET_VALUE", "SET_VALUE" }, session.Gateway.RequestNames);
            Assert.Equal(new byte[] { 17, 2 }, session.Gateway.Requests[0].Payload);
            Assert.Equal(new byte[] { 17, 255 }, session.Gateway.Requests[1].Payload);
        }

        [Fact]
        public async Task RenameGroupSendsTheGroupIdAndTheName()
        {
            using var session = await GatewayTestSession.AsAdminAsync();

            await session.Client.RenameGroupAsync(1, "Hoftor");

            FakeRequest request = Assert.Single(session.Gateway.Requests);
            Assert.Equal(Command.SetGroupName, request.Command);
            Assert.Equal(Bytes(1, "Hoftor"), request.Payload);
            Assert.Equal("Hoftor", session.Gateway.Groups.Single(g => g.Id == 1).Name);
        }

        [Fact]
        public async Task DeleteGroupRemovesThePortsAndThenTheGroup()
        {
            using var session = await GatewayTestSession.AsAdminAsync();
            Group gate = await LoadGroupAsync(session, "Gate");

            await session.Client.DeleteGroupAsync(gate);

            Assert.Equal(new[] { "REMOVE_PORT", "REMOVE_PORT", "REMOVE_GROUP" }, session.Gateway.RequestNames);
            Assert.Equal(new[] { "01", "02", "01" }, session.Gateway.Requests.Select(r => r.Hex));
            Assert.Equal(new[] { "Garage" }, session.Gateway.Groups.Select(g => g.Name));
            Assert.Equal(new[] { 0 }, session.Gateway.Ports.Keys);
        }

        [Fact]
        public async Task DeleteGroupToleratesAPortTheGatewayNoLongerKnows()
        {
            using var session = await GatewayTestSession.AsAdminAsync();
            Group gate = await LoadGroupAsync(session, "Gate");
            session.Gateway.Ports.Remove(1);

            await session.Client.DeleteGroupAsync(gate);

            Assert.Equal(new[] { "REMOVE_PORT", "REMOVE_PORT", "REMOVE_GROUP" }, session.Gateway.RequestNames);
            Assert.Equal(new[] { "Garage" }, session.Gateway.Groups.Select(g => g.Name));
            Assert.Equal(new[] { 0 }, session.Gateway.Ports.Keys);
        }

        [Fact]
        public async Task DeleteGroupStopsOnAnyOtherError()
        {
            using var session = await GatewayTestSession.AsAdminAsync();
            Group gate = await LoadGroupAsync(session, "Gate");
            session.Gateway.FailNext(Command.RemovePort, Error.PortError);

            GatewayException e = await Assert.ThrowsAsync<GatewayException>(() => session.Client.DeleteGroupAsync(gate));

            Assert.Equal(Error.PortError, e.Error);
            Assert.Equal(new[] { "REMOVE_PORT" }, session.Gateway.RequestNames);
            Assert.Equal(2, session.Gateway.Groups.Count);
        }

        [Fact]
        public async Task PortCountIsTheNumberOfPayloadBytes()
        {
            using var session = await GatewayTestSession.AsAdminAsync();

            Assert.Equal(3, await session.Client.GetPortCountAsync());
        }

        [Fact]
        public async Task AddPortSendsAddPortThenSetType()
        {
            using var session = await GatewayTestSession.AsAdminAsync();

            Port port = await session.Client.AddPortAsync(PortType.Light);

            // Ports 0 to 2 exist, so the fake hands out 3.
            Assert.Equal(new[] { "GET_PORTS", "ADD_PORT", "SET_TYPE" }, session.Gateway.RequestNames);
            Assert.Equal(new byte[] { 3, (byte)PortType.Light }, session.Gateway.Requests[2].Payload);
            Assert.Equal(3, port.Id);
            Assert.Equal(PortType.Light, port.Type);
            Assert.Equal((int)PortType.Light, session.Gateway.Ports[3]);
        }

        [Fact]
        public async Task LearnPortSendsInheritPortThenSetType()
        {
            using var session = await GatewayTestSession.AsAdminAsync();

            Port port = await session.Client.LearnPortAsync(PortType.Down);

            Assert.Equal(new[] { "GET_PORTS", "INHERIT_PORT", "SET_TYPE" }, session.Gateway.RequestNames);
            Assert.Equal(new byte[] { 3, (byte)PortType.Down }, session.Gateway.Requests[2].Payload);
            Assert.Equal(3, port.Id);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task GatewayWithSixteenPortsIsNotAskedForAnotherOne(bool learn)
        {
            using var session = await GatewayTestSession.AsAdminAsync();
            for (int portId = 0; portId < 16; portId++)
                session.Gateway.Ports[portId] = (int)PortType.Impuls;

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => learn ? session.Client.LearnPortAsync(PortType.Impuls) : session.Client.AddPortAsync(PortType.Impuls));

            Assert.Equal(new[] { "GET_PORTS" }, session.Gateway.RequestNames);
        }

        [Fact]
        public async Task SmartKeyGroupCannotLearnAPortFromTheDrive()
        {
            using var session = await GatewayTestSession.AsAdminAsync();
            session.Gateway.Values[1] = (int)GroupType.Smartkey;
            Group smartKey = await LoadGroupAsync(session, "Gate");

            await Assert.ThrowsAsync<NotSupportedException>(() => session.Client.AddPortToGroupAsync(smartKey, PortType.Lock, learn: true));
            await session.Client.PingAsync();

            Assert.Equal(new[] { "PING" }, session.Gateway.RequestNames);

            // Copying from a hand transmitter is still allowed for the same group.
            Port port = await session.Client.AddPortToGroupAsync(smartKey, PortType.Lock, learn: false);

            Assert.Equal(3, port.Id);
            Assert.Contains("ADD_PORT", session.Gateway.RequestNames);
        }

        [Theory]
        [InlineData(false, "ADD_PORT")]
        [InlineData(true, "INHERIT_PORT")]
        public async Task AddPortToGroupSendsTheExistingPortsPlusTheNewOne(bool learn, string createCommand)
        {
            using var session = await GatewayTestSession.AsAdminAsync();
            Group gate = await LoadGroupAsync(session, "Gate");

            Port port = await session.Client.AddPortToGroupAsync(gate, PortType.Half, learn);

            Assert.Equal(new[] { "GET_PORTS", createCommand, "SET_TYPE", "SET_GROUPED_PORTS" }, session.Gateway.RequestNames);
            Assert.Equal(new byte[] { 1, 1, 2, 3 }, session.Gateway.Requests[3].Payload);

            Assert.Equal(3, port.Id);
            Assert.Equal(PortType.Half, port.Type);
            Assert.Equal(new[] { 1, 2, 3 }, gate.Ports.Select(p => p.Id));
            Assert.Equal(new[] { 1, 2, 3 }, session.Gateway.Groups.Single(g => g.Id == 1).Ports);
        }
    }
}
