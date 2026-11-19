using BiSec.Library;
using BiSec.Library.Exceptions;
using BiSec.Library.Models;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace tests
{
    public class GatewayClientStateTests
    {
        private static Group GateGroup(int? requestablePortId)
        {
            return new Group
            {
                Id = 1,
                Name = "Gate",
                Type = GroupType.SlidingGate,
                Ports = new[] { new Port { Id = 1, Type = PortType.Impuls }, new Port { Id = 2, Type = PortType.Up } },
                RequestablePortId = requestablePortId,
            };
        }

        [Fact]
        public async Task SetStateReturnsTheTransitionFromAnHmGetTransitionAnswer()
        {
            using var session = await GatewayTestSession.AsAdminAsync();
            session.Gateway.SetTransitions(1, FakeGateway.TransitionPayload(40, 200, 12));

            Transition transition = await session.Client.SetStateAsync(1);

            FakeRequest request = Assert.Single(session.Gateway.Requests);
            Assert.Equal(Command.SetState, request.Command);
            Assert.Equal(new byte[] { 1, 0xFF }, request.Payload);

            Assert.NotNull(transition);
            Assert.Equal(40, transition.ActualState);
            Assert.Equal(200, transition.RequestedState);
            Assert.Equal(12, transition.DriveTime);
        }

        [Fact]
        public async Task SetStateReturnsNullForAPlainAcknowledgement()
        {
            using var session = await GatewayTestSession.AsAdminAsync();
            session.Gateway.SetStateReply = SetStateReply.Acknowledge;

            Transition transition = await session.Client.SetStateAsync(new Port { Id = 2, Type = PortType.Up });

            Assert.Null(transition);
            FakeRequest request = Assert.Single(session.Gateway.Requests);
            Assert.Equal(Command.SetState, request.Command);
            Assert.Equal(new byte[] { 2, 0xFF }, request.Payload);
        }

        [Fact]
        public async Task SetStateWhileTheAdapterIsBusyThrows()
        {
            using var session = await GatewayTestSession.AsAdminAsync();
            session.Gateway.FailNext(Command.SetState, Error.AdapterBusy);

            GatewayException e = await Assert.ThrowsAsync<GatewayException>(() => session.Client.SetStateAsync(1));

            Assert.Equal(Error.AdapterBusy, e.Error);
            Assert.Equal(Command.SetState, e.Command);
        }

        [Fact]
        public async Task GroupStateIsRequestedThroughTheRequestablePort()
        {
            using var session = await GatewayTestSession.AsAdminAsync();
            session.Gateway.SetTransitions(1, FakeGateway.TransitionPayload(0, 0, 0));
            session.Gateway.SetTransitions(2, FakeGateway.TransitionPayload(200, 200, 0));

            Transition transition = await session.Client.GetTransitionAsync(GateGroup(requestablePortId: 2));

            FakeRequest request = Assert.Single(session.Gateway.Requests);
            Assert.Equal(Command.GetTransition, request.Command);
            Assert.Equal(new byte[] { 2 }, request.Payload);
            Assert.Equal(200, transition.ActualState);
        }

        [Fact]
        public async Task GroupWithoutRequestablePortCannotBeAskedForItsState()
        {
            using var session = await GatewayTestSession.AsAdminAsync();

            await Assert.ThrowsAsync<InvalidOperationException>(() => session.Client.GetTransitionAsync(GateGroup(requestablePortId: null)));
            await session.Client.PingAsync();

            Assert.Equal(new[] { "PING" }, session.Gateway.RequestNames);
        }

        [Fact]
        public async Task FailedGroupStateRequestIsRepeated()
        {
            using var session = await GatewayTestSession.AsAdminAsync();
            session.Gateway.SetTransitions(2, FakeGateway.TransitionPayload(200, 200, 0));
            session.Gateway.FailNext(Command.GetTransition, Error.PortError, times: 2);

            Transition transition = await session.Client.GetTransitionAsync(GateGroup(requestablePortId: 2));

            Assert.Equal(200, transition.ActualState);
            Assert.Equal(3, session.Gateway.Requests.Count);
        }

        [Fact]
        public async Task GroupStateRequestGivesUpAfterTheThirdAttempt()
        {
            using var session = await GatewayTestSession.AsAdminAsync();
            session.Gateway.FailNext(Command.GetTransition, Error.PortError, times: 5);

            GatewayException e = await Assert.ThrowsAsync<GatewayException>(() => session.Client.GetTransitionAsync(GateGroup(requestablePortId: 2)));

            Assert.Equal(Error.PortError, e.Error);
            Assert.Equal(new[] { "HM_GET_TRANSITION", "HM_GET_TRANSITION", "HM_GET_TRANSITION" }, session.Gateway.RequestNames);
        }

        [Fact]
        public async Task UnansweredGroupStateRequestIsRepeated()
        {
            using var session = await GatewayTestSession.AsAdminAsync();
            session.Connection.Timeouts.Set(Command.GetTransition, 250);
            session.Gateway.SetTransitions(2, FakeGateway.TransitionPayload(200, 200, 0));
            session.Gateway.IgnoreNext(Command.GetTransition);

            Transition transition = await session.Client.GetTransitionAsync(GateGroup(requestablePortId: 2));

            Assert.Equal(200, transition.ActualState);
            Assert.Equal(2, session.Gateway.Requests.Count);
        }

        [Fact]
        public async Task DeniedGroupStateRequestIsNotRepeated()
        {
            using var session = await GatewayTestSession.AsAdminAsync();
            session.Client.AutoRelogin = false;
            session.Gateway.FailNext(Command.GetTransition, Error.PermissionDenied, times: 3);

            GatewayException e = await Assert.ThrowsAsync<GatewayException>(() => session.Client.GetTransitionAsync(GateGroup(requestablePortId: 2)));

            Assert.Equal(Error.PermissionDenied, e.Error);
            Assert.Single(session.Gateway.Requests);
        }

        [Fact]
        public async Task TruncatedTransitionIsAProtocolError()
        {
            using var session = await GatewayTestSession.AsAdminAsync();
            session.Gateway.ReplyNext(Command.GetTransition, Command.GetTransition, FakeGateway.TransitionPayload(0, 0, 0).Take(13).ToArray());

            await Assert.ThrowsAsync<GatewayProtocolException>(() => session.Client.GetTransitionAsync(1));
        }
    }
}
