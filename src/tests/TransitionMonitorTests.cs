using BiSec.Library;
using BiSec.Library.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace tests
{
    /// <summary>
    /// The monitor's waiting is replaced by a recorder, so no test waits for a drive.
    /// </summary>
    public class TransitionMonitorTests
    {
        private static readonly TimeSpan Guard = TimeSpan.FromSeconds(10);

        private static readonly Port GaragePort = new Port { Id = 0, Type = PortType.Impuls };

        private static Group Garage()
        {
            return new Group
            {
                Id = 0,
                Name = "Garage",
                Type = GroupType.SectionalDoor,
                Ports = new[] { GaragePort },
                RequestablePortId = 0,
            };
        }

        private static TransitionMonitor Monitor(GatewayTestSession session, List<TimeSpan> delays)
        {
            return new TransitionMonitor(session.Client)
            {
                Delay = (delay, cancellationToken) =>
                {
                    delays.Add(delay);
                    return Task.CompletedTask;
                },
            };
        }

        [Fact]
        public async Task AsksAgainAfterTheReportedDriveTimeUntilTheStateIsSettled()
        {
            using var session = await GatewayTestSession.AsAdminAsync();
            session.Gateway.SetTransitions(0,
                FakeGateway.TransitionPayload(0, 200, 7),
                FakeGateway.TransitionPayload(120, 200, 3),
                FakeGateway.TransitionPayload(200, 200, 0));

            var delays = new List<TimeSpan>();
            var seen = new List<Transition>();

            Transition settled = await Monitor(session, delays).FollowAsync(Garage(), seen.Add).WaitAsync(Guard);

            Assert.Equal(new[] { TimeSpan.FromSeconds(7), TimeSpan.FromSeconds(3) }, delays);
            Assert.Equal(new[] { 0, 120, 200 }, seen.Select(t => t.ActualState));
            Assert.Same(seen[2], settled);
            Assert.Equal(new[] { "00", "00", "00" }, session.Gateway.RequestsFor(Command.GetTransition).Select(r => r.Hex));
            Assert.Equal(3, session.Gateway.Requests.Count);
        }

        [Fact]
        public async Task AsksEveryFiveSecondsWhileAutoCloseIsSet()
        {
            using var session = await GatewayTestSession.AsAdminAsync();
            session.Gateway.SetTransitions(0,
                // Auto-close wins over the reported drive time.
                FakeGateway.TransitionPayload(200, 0, 30, autoClose: true),
                FakeGateway.TransitionPayload(200, 0, 0, autoClose: true),
                FakeGateway.TransitionPayload(0, 0, 0));

            var delays = new List<TimeSpan>();

            Transition settled = await Monitor(session, delays).FollowAsync(Garage()).WaitAsync(Guard);

            Assert.Equal(new[] { TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5) }, delays);
            Assert.False(settled.AutoClose);
            Assert.Equal(0, settled.ActualState);
            Assert.Equal(3, session.Gateway.Requests.Count);
        }

        [Fact]
        public async Task DoesNotWaitWhenTheStateIsAlreadySettled()
        {
            using var session = await GatewayTestSession.AsAdminAsync();
            session.Gateway.SetTransitions(0, FakeGateway.TransitionPayload(200, 200, 0));

            var delays = new List<TimeSpan>();
            var seen = new List<Transition>();

            Transition settled = await Monitor(session, delays).FollowAsync(Garage(), seen.Add).WaitAsync(Guard);

            Assert.Empty(delays);
            Assert.Same(Assert.Single(seen), settled);
            Assert.Single(session.Gateway.Requests);
        }

        [Fact]
        public async Task StopsWhenTheDriveReportsAnError()
        {
            using var session = await GatewayTestSession.AsAdminAsync();
            session.Gateway.SetTransitions(0, FakeGateway.TransitionPayload(80, 200, 9, error: true));

            var delays = new List<TimeSpan>();

            Transition result = await Monitor(session, delays).FollowAsync(Garage()).WaitAsync(Guard);

            Assert.True(result.Error);
            Assert.Empty(delays);
            Assert.Single(session.Gateway.Requests);
        }

        [Fact]
        public async Task FollowsTheTransitionReturnedBySetState()
        {
            using var session = await GatewayTestSession.AsAdminAsync();
            session.Gateway.SetTransitions(0,
                FakeGateway.TransitionPayload(0, 200, 4),
                FakeGateway.TransitionPayload(200, 200, 0));

            var delays = new List<TimeSpan>();
            var seen = new List<Transition>();

            Transition settled = await Monitor(session, delays).SetStateAndFollowAsync(Garage(), GaragePort, seen.Add).WaitAsync(Guard);

            Assert.Equal(new[] { TimeSpan.FromSeconds(4) }, delays);
            Assert.Equal(new[] { "SET_STATE", "HM_GET_TRANSITION" }, session.Gateway.RequestNames);
            Assert.Equal(new[] { 0, 200 }, seen.Select(t => t.ActualState));
            Assert.Same(seen[1], settled);
        }

        [Fact]
        public async Task WaitsFiveSecondsAfterASetStateThatIsOnlyAcknowledged()
        {
            using var session = await GatewayTestSession.AsAdminAsync();
            session.Gateway.SetStateReply = SetStateReply.Acknowledge;
            session.Gateway.SetTransitions(0, FakeGateway.TransitionPayload(200, 200, 0));

            var delays = new List<TimeSpan>();
            var seen = new List<Transition>();

            Transition settled = await Monitor(session, delays).SetStateAndFollowAsync(Garage(), GaragePort, seen.Add).WaitAsync(Guard);

            Assert.Equal(new[] { TimeSpan.FromSeconds(5) }, delays);
            Assert.Equal(new[] { "SET_STATE", "HM_GET_TRANSITION" }, session.Gateway.RequestNames);
            Assert.Same(Assert.Single(seen), settled);
            Assert.Equal(200, settled.ActualState);
        }

        [Fact]
        public async Task RefusesToTriggerAPortOfAGroupThatCannotBeFollowed()
        {
            using var session = await GatewayTestSession.AsAdminAsync();
            Group group = Garage();
            group.RequestablePortId = null;

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => Monitor(session, new List<TimeSpan>()).SetStateAndFollowAsync(group, GaragePort).WaitAsync(Guard));

            // The drive must not have been started.
            Assert.Empty(session.Gateway.Requests);
        }

        [Theory]
        // Cancelled while waiting for the drive.
        [InlineData(true)]
        // The wait ends normally, but the token is cancelled before the next request.
        [InlineData(false)]
        public async Task StopsOnCancellation(bool delayObservesCancellation)
        {
            using var session = await GatewayTestSession.AsAdminAsync();
            // The drive never settles.
            session.Gateway.SetTransitions(0, FakeGateway.TransitionPayload(10, 200, 2));

            using var cancellation = new CancellationTokenSource();
            int delays = 0;
            var monitor = new TransitionMonitor(session.Client)
            {
                Delay = (delay, cancellationToken) =>
                {
                    if (++delays == 2)
                        cancellation.Cancel();

                    return delayObservesCancellation && cancellationToken.IsCancellationRequested
                        ? Task.FromCanceled(cancellationToken)
                        : Task.CompletedTask;
                },
            };

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => monitor.FollowAsync(Garage(), null, cancellation.Token).WaitAsync(Guard));

            Assert.Equal(2, delays);
            Assert.Equal(2, session.Gateway.Requests.Count);
        }
    }
}
