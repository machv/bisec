using BiSec.Library;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Xunit;

namespace tests
{
    public class GatewayConnectionTests
    {
        // Only there so a hanging library call fails the test instead of blocking the run.
        private static readonly TimeSpan Guard = TimeSpan.FromSeconds(10);

        private static Message Request(Command command, Payload payload = null)
        {
            return new Message
            {
                Command = command,
                Payload = payload ?? PayloadFactory.Empty,
            };
        }

        private static Message Ping()
        {
            return Request(Command.Ping);
        }

        private static async Task<GatewayConnection> ConnectAsync(FakeGateway gateway)
        {
            GatewayConnection connection = GatewayTestSession.CreateConnection(gateway);
            await connection.ConnectAsync().WaitAsync(Guard);

            return connection;
        }

        private static Task<Package> LoginAsync(GatewayConnection connection)
        {
            return connection.SendAsync(Request(Command.Login, PayloadFactory.Login(GatewayTestSession.AdminName, GatewayTestSession.AdminPassword)));
        }

        private static Channel<PackageReceivedEventArgs> Record(GatewayConnection connection)
        {
            Channel<PackageReceivedEventArgs> events = Channel.CreateUnbounded<PackageReceivedEventArgs>();
            connection.PackageReceived += (sender, e) => events.Writer.TryWrite(e);

            return events;
        }

        private static Task<PackageReceivedEventArgs> NextAsync(Channel<PackageReceivedEventArgs> events)
        {
            return events.Reader.ReadAsync().AsTask().WaitAsync(Guard);
        }

        [Fact]
        public async Task ConnectReturnsAndAPingRoundTrips()
        {
            using var gateway = new FakeGateway();
            using var connection = GatewayTestSession.CreateConnection(gateway);

            // Connect() used to block forever, so it runs on another thread against a deadline.
            await Task.Run(() => connection.Connect()).WaitAsync(Guard);
            Assert.True(connection.Connected);

            Package response = await connection.SendAsync(Ping()).WaitAsync(Guard);

            Assert.Equal(Command.Ping, response.Message.Command);
            Assert.True(response.Message.IsResponse);

            FakeRequest request = Assert.Single(gateway.Requests);
            Assert.Equal(Command.Ping, request.Command);
            Assert.False(request.IsResponse);
            Assert.Empty(request.Payload);
            Assert.Equal(request.Tag, response.Message.Tag);
            Assert.Equal(FakeGateway.GatewayId, request.Receiver);
            Assert.True(request.ChecksumsValid);
        }

        [Fact]
        public async Task SendingBeforeConnectingIsRefused()
        {
            using var gateway = new FakeGateway();
            using var connection = GatewayTestSession.CreateConnection(gateway);

            await Assert.ThrowsAsync<InvalidOperationException>(() => connection.SendAsync(Ping()));

            Assert.False(connection.Connected);
            Assert.Equal(0, gateway.ConnectionCount);
        }

        [Fact]
        public async Task ConnectingToAClosedPortFailsAndLeavesTheConnectionClosed()
        {
            using var refusing = new RefusingPort();
            using var connection = new GatewayConnection(IPAddress.Loopback, null, FakeGateway.GatewayId, refusing.Port);

            await Assert.ThrowsAnyAsync<SocketException>(() => connection.ConnectAsync().WaitAsync(Guard));

            Assert.False(connection.Connected);
        }

        [Fact]
        public async Task UnansweredRequestTimesOutAndTheNextRequestWorks()
        {
            using var gateway = new FakeGateway();
            using var connection = await ConnectAsync(gateway);
            connection.Timeouts.Set(Command.Ping, 250);
            gateway.IgnoreNext(Command.Ping);

            Stopwatch stopwatch = Stopwatch.StartNew();
            await Assert.ThrowsAsync<TimeoutException>(() => connection.SendAsync(Ping()));
            stopwatch.Stop();

            Assert.InRange(stopwatch.ElapsedMilliseconds, 200, 4000);
            Assert.True(connection.Connected);

            Package response = await connection.SendAsync(Ping());

            Assert.Equal(Command.Ping, response.Message.Command);
            Assert.Equal(2, gateway.Requests.Count);
        }

        [Fact]
        public async Task LateAnswerToATimedOutRequestDoesNotCompleteTheNextRequest()
        {
            using var gateway = new FakeGateway();
            using var connection = await ConnectAsync(gateway);
            connection.Timeouts.Set(Command.Ping, 250);
            gateway.IgnoreNext(Command.Ping);
            gateway.IgnoreNext(Command.GetName);

            await Assert.ThrowsAsync<TimeoutException>(() => connection.SendAsync(Ping()));
            FakeRequest timedOut = Assert.Single(gateway.Requests);

            Task<Package> pending = connection.SendAsync(Request(Command.GetName));
            FakeRequest current = await gateway.WaitForRequestAsync(Command.GetName);

            // The gateway answers the first request after all, and only then the second one.
            await gateway.PushAsync(Command.Ping, tag: timedOut.Tag);
            await gateway.PushAsync(Command.GetName, Encoding.UTF8.GetBytes("Garage GW"), tag: current.Tag);

            Package response = await pending.WaitAsync(Guard);

            Assert.Equal(Command.GetName, response.Message.Command);
            Assert.Equal("Garage GW", response.Message.Payload.TextContent);
        }

        [Fact]
        public async Task TagsStayInTheProtocolRangeAfterManyRequests()
        {
            using var gateway = new FakeGateway();
            using var connection = await ConnectAsync(gateway);

            for (int i = 0; i < 135; i++)
                await connection.SendAsync(Ping()).WaitAsync(Guard);

            Assert.Equal(135, gateway.Requests.Count);
            Assert.All(gateway.Requests, request => Assert.InRange((int)request.Tag, 0, 127));
        }

        [Fact]
        public async Task ConnectIsRepeatedWhileTheGatewayIsUnreachable()
        {
            using var gateway = new FakeGateway();
            using var connection = GatewayTestSession.CreateConnection(gateway);

            int attempts = 0;
            var connect = connection.ConnectSocketAsync;
            connection.ConnectSocketAsync = (client, address, port, cancellationToken) =>
            {
                if (++attempts <= 2)
                    throw new SocketException((int)SocketError.HostUnreachable);

                return connect(client, address, port, cancellationToken);
            };

            await connection.ConnectAsync().WaitAsync(Guard);
            Package response = await connection.SendAsync(Ping()).WaitAsync(Guard);

            Assert.Equal(3, attempts);
            Assert.Equal(Command.Ping, response.Message.Command);
        }

        [Fact]
        public async Task ConnectGivesUpAfterTheTimeoutWhenTheGatewayStaysUnreachable()
        {
            using var gateway = new FakeGateway();
            using var connection = GatewayTestSession.CreateConnection(gateway);
            connection.ConnectTimeout = TimeSpan.FromMilliseconds(600);

            int attempts = 0;
            connection.ConnectSocketAsync = (client, address, port, cancellationToken) =>
            {
                attempts++;
                throw new SocketException((int)SocketError.HostUnreachable);
            };

            TimeoutException failure = await Assert.ThrowsAsync<TimeoutException>(() => connection.ConnectAsync().WaitAsync(Guard));

            Assert.Contains("Last error", failure.Message);
            Assert.True(attempts > 1);
            Assert.False(connection.Connected);
        }

        [Fact]
        public async Task UnansweredConnectAttemptIsReplacedByANewOne()
        {
            using var gateway = new FakeGateway();
            using var connection = GatewayTestSession.CreateConnection(gateway);
            connection.ConnectAttemptTimeout = TimeSpan.FromMilliseconds(150);

            int attempts = 0;
            var connect = connection.ConnectSocketAsync;
            connection.ConnectSocketAsync = (client, address, port, cancellationToken) =>
            {
                // The first two attempts hang the way a connect does while nothing answers.
                if (++attempts <= 2)
                    return Task.Delay(Timeout.Infinite, cancellationToken);

                return connect(client, address, port, cancellationToken);
            };

            await connection.ConnectAsync().WaitAsync(Guard);
            Package response = await connection.SendAsync(Ping()).WaitAsync(Guard);

            Assert.Equal(3, attempts);
            Assert.Equal(Command.Ping, response.Message.Command);
        }

        [Fact]
        public async Task ConnectGivesUpAfterTheTimeoutWhenNoAttemptIsAnswered()
        {
            using var gateway = new FakeGateway();
            using var connection = GatewayTestSession.CreateConnection(gateway);
            connection.ConnectTimeout = TimeSpan.FromMilliseconds(500);
            connection.ConnectAttemptTimeout = TimeSpan.FromMilliseconds(150);

            int attempts = 0;
            connection.ConnectSocketAsync = (client, address, port, cancellationToken) =>
            {
                attempts++;
                return Task.Delay(Timeout.Infinite, cancellationToken);
            };

            TimeoutException failure = await Assert.ThrowsAsync<TimeoutException>(() => connection.ConnectAsync().WaitAsync(Guard));

            Assert.Contains("500 ms", failure.Message);
            Assert.True(attempts > 1);
            Assert.False(connection.Connected);
        }

        [Fact]
        public async Task ConnectIsNotRepeatedWhenTheGatewayRefuses()
        {
            using var gateway = new FakeGateway();
            using var connection = GatewayTestSession.CreateConnection(gateway);

            int attempts = 0;
            connection.ConnectSocketAsync = (client, address, port, cancellationToken) =>
            {
                attempts++;
                throw new SocketException((int)SocketError.ConnectionRefused);
            };

            SocketException failure = await Assert.ThrowsAsync<SocketException>(() => connection.ConnectAsync().WaitAsync(Guard));

            Assert.Equal(SocketError.ConnectionRefused, failure.SocketErrorCode);
            Assert.Equal(1, attempts);
        }

        [Fact]
        public async Task PackageThatStopsArrivingClosesTheConnectionAndFailsThePendingRequest()
        {
            using var gateway = new FakeGateway();
            using var connection = GatewayTestSession.CreateConnection(gateway);
            connection.PackageCompletionTimeout = TimeSpan.FromMilliseconds(200);
            await connection.ConnectAsync().WaitAsync(Guard);

            gateway.IgnoreNext(Command.Ping);
            Task<Package> pending = connection.SendAsync(Ping());
            FakeRequest request = await gateway.WaitForRequestAsync(Command.Ping);

            string answer = FakeGateway.Frame(Command.Ping, new byte[0], request.Tag);
            await gateway.WriteRawAsync(answer.Substring(0, answer.Length - 6));

            // Without a limit this would sit until the request timeout and leave the stream out of step.
            await Assert.ThrowsAsync<IOException>(() => pending.WaitAsync(Guard));
            Assert.False(connection.Connected);
        }

        [Fact]
        public async Task PackageArrivingInPiecesIsDelivered()
        {
            using var gateway = new FakeGateway();
            using var connection = GatewayTestSession.CreateConnection(gateway);
            connection.PackageCompletionTimeout = TimeSpan.FromSeconds(2);
            await connection.ConnectAsync().WaitAsync(Guard);

            gateway.IgnoreNext(Command.Ping);
            Task<Package> pending = connection.SendAsync(Ping());
            FakeRequest request = await gateway.WaitForRequestAsync(Command.Ping);

            string answer = FakeGateway.Frame(Command.Ping, new byte[0], request.Tag);
            await gateway.WriteRawAsync(answer.Substring(0, 10));
            await Task.Delay(100);
            await gateway.WriteRawAsync(answer.Substring(10, 25));
            await Task.Delay(100);
            await gateway.WriteRawAsync(answer.Substring(35));

            Package response = await pending.WaitAsync(Guard);
            Assert.Equal(Command.Ping, response.Message.Command);
        }

        [Fact]
        public async Task ConcurrentRequestsEachGetTheirOwnResponse()
        {
            using var gateway = new FakeGateway();
            using var connection = await ConnectAsync(gateway);

            Command[] commands = Enumerable.Range(0, 20).Select(i => i % 2 == 0 ? Command.Ping : Command.GetName).ToArray();
            Task<Package>[] requests = commands.Select(command => connection.SendAsync(Request(command))).ToArray();

            Package[] responses = await Task.WhenAll(requests).WaitAsync(Guard);

            Assert.Equal(commands, responses.Select(r => r.Message.Command));
            Assert.Equal(20, gateway.Requests.Count);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task DroppedConnectionFailsThePendingRequestAndCanBeReopened(bool reset)
        {
            using var gateway = new FakeGateway();
            using var connection = await ConnectAsync(gateway);

            int disconnects = 0;
            var disconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            connection.Disconnected += (sender, e) =>
            {
                Interlocked.Increment(ref disconnects);
                disconnected.TrySetResult();
            };

            gateway.IgnoreNext(Command.Ping);
            Task<Package> pending = connection.SendAsync(Ping());
            await gateway.WaitForRequestAsync(Command.Ping);

            Stopwatch stopwatch = Stopwatch.StartNew();
            gateway.DropConnection(reset);

            await Assert.ThrowsAsync<IOException>(() => pending.WaitAsync(Guard));
            stopwatch.Stop();

            // Well before the request timeout of five seconds.
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2), $"The request failed after {stopwatch.Elapsed}.");
            await disconnected.Task.WaitAsync(Guard);
            Assert.False(connection.Connected);

            await Assert.ThrowsAsync<InvalidOperationException>(() => connection.SendAsync(Ping()));

            await connection.ConnectAsync().WaitAsync(Guard);
            Assert.True(connection.Connected);

            Package response = await connection.SendAsync(Ping()).WaitAsync(Guard);

            Assert.Equal(Command.Ping, response.Message.Command);
            Assert.Equal(2, gateway.ConnectionCount);
            Assert.Equal(1, Volatile.Read(ref disconnects));
        }

        [Fact]
        public async Task LocalDisconnectFailsThePendingRequest()
        {
            using var gateway = new FakeGateway();
            using var connection = await ConnectAsync(gateway);
            gateway.IgnoreNext(Command.Ping);

            Task<Package> pending = connection.SendAsync(Ping());
            await gateway.WaitForRequestAsync(Command.Ping);

            connection.Disconnect();

            await Assert.ThrowsAsync<IOException>(() => pending.WaitAsync(Guard));
            Assert.False(connection.Connected);
        }

        [Fact]
        public async Task CancellingARequestLeavesTheConnectionUsable()
        {
            using var gateway = new FakeGateway();
            using var connection = await ConnectAsync(gateway);
            using var cancellation = new CancellationTokenSource();
            gateway.IgnoreNext(Command.Ping);

            Task<Package> pending = connection.SendAsync(Ping(), cancellation.Token);
            await gateway.WaitForRequestAsync(Command.Ping);
            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(Guard));

            Package response = await connection.SendAsync(Request(Command.GetName)).WaitAsync(Guard);

            Assert.Equal(Command.GetName, response.Message.Command);
        }

        [Fact]
        public async Task FrameWithNonHexContentIsSkippedAndTheFollowingResponseArrives()
        {
            using var gateway = new FakeGateway();
            using var connection = await ConnectAsync(gateway);
            gateway.IgnoreNext(Command.Ping);

            Task<Package> pending = connection.SendAsync(Ping());
            FakeRequest request = await gateway.WaitForRequestAsync(Command.Ping);

            // A valid header announcing an 11-byte message, then exactly that many characters that are not hex.
            // The response follows in the same write, so it is only found if the broken frame is skipped by its length.
            string broken = FakeGateway.GatewayId + FakeGateway.AssignedClientId + "000B" + new string('Z', (11 - 2 + 1) * 2);
            await gateway.WriteRawAsync(broken + FakeGateway.Frame(Command.Ping, null, request.Tag));

            Package response = await pending.WaitAsync(Guard);

            Assert.Equal(Command.Ping, response.Message.Command);
            Assert.True(connection.Connected);
        }

        [Fact]
        public async Task ResponseArrivingInSeveralPiecesIsReassembled()
        {
            using var gateway = new FakeGateway();
            using var connection = await ConnectAsync(gateway);
            gateway.IgnoreNext(Command.GetName);

            Task<Package> pending = connection.SendAsync(Request(Command.GetName));
            FakeRequest request = await gateway.WaitForRequestAsync(Command.GetName);

            // Cut inside the header, inside the length field and inside the payload.
            string frame = FakeGateway.Frame(Command.GetName, Encoding.UTF8.GetBytes("Garage GW"), request.Tag);
            foreach (string piece in new[] { frame.Substring(0, 7), frame.Substring(7, 19), frame.Substring(26, 20), frame.Substring(46) })
            {
                await gateway.WriteRawAsync(piece);
                await Task.Delay(20);
            }

            Package response = await pending.WaitAsync(Guard);

            Assert.Equal("Garage GW", response.Message.Payload.TextContent);
        }

        [Fact]
        public async Task FailingPackageReceivedHandlerDoesNotStopTheConnection()
        {
            using var gateway = new FakeGateway();
            using var connection = await ConnectAsync(gateway);
            connection.PackageReceived += (sender, e) => throw new InvalidOperationException("handler failure");

            await connection.SendAsync(Ping()).WaitAsync(Guard);
            Package response = await connection.SendAsync(Request(Command.GetName)).WaitAsync(Guard);

            Assert.Equal(Command.GetName, response.Message.Command);
            Assert.True(connection.Connected);
        }

        [Theory]
        [InlineData("this is not a bisecur package header")]
        // Hex text, but a message cannot be one byte long.
        [InlineData("5410EC852A310000000000060001")]
        public async Task GarbageHeaderClosesTheConnectionAndFailsThePendingRequest(string garbage)
        {
            using var gateway = new FakeGateway();
            using var connection = await ConnectAsync(gateway);

            var disconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            connection.Disconnected += (sender, e) => disconnected.TrySetResult();

            gateway.IgnoreNext(Command.Ping);
            Task<Package> pending = connection.SendAsync(Ping());
            await gateway.WaitForRequestAsync(Command.Ping);

            await gateway.WriteRawAsync(garbage);

            await Assert.ThrowsAsync<IOException>(() => pending.WaitAsync(Guard));
            await disconnected.Task.WaitAsync(Guard);
            Assert.False(connection.Connected);
        }

        [Fact]
        public async Task PushedPackageIsReportedAsUnsolicitedAndDoesNotCompleteThePendingRequest()
        {
            using var gateway = new FakeGateway();
            using var connection = await ConnectAsync(gateway);
            Channel<PackageReceivedEventArgs> events = Record(connection);
            gateway.IgnoreNext(Command.Ping);

            Task<Package> pending = connection.SendAsync(Ping());
            FakeRequest request = await gateway.WaitForRequestAsync(Command.Ping);

            // The same command as the pending request, so only the tag tells the two apart.
            await gateway.PushAsync(Command.Ping, new byte[] { 0x2A });

            PackageReceivedEventArgs pushed = await NextAsync(events);
            Assert.True(pushed.IsUnsolicited);
            Assert.Equal(FakeGateway.BroadcastTag, pushed.Package.Message.Tag);
            Assert.Equal(new byte[] { 0x2A }, pushed.Package.Message.Payload.ToByteArray());
            Assert.False(pending.IsCompleted);

            await gateway.PushAsync(Command.Ping, tag: request.Tag);

            Package response = await pending.WaitAsync(Guard);
            Assert.Empty(response.Message.Payload.ToByteArray());

            PackageReceivedEventArgs answered = await NextAsync(events);
            Assert.False(answered.IsUnsolicited);
            Assert.Same(response, answered.Package);
        }

        [Fact]
        public async Task PushedLogoutRaisesLoggedOutAndEndsTheAuthentication()
        {
            using var gateway = new FakeGateway();
            using var connection = await ConnectAsync(gateway);
            await LoginAsync(connection);
            Assert.True(connection.IsAuthenticated);

            var loggedOut = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            connection.LoggedOut += (sender, e) => loggedOut.TrySetResult();

            await gateway.PushAsync(Command.Logout);
            await loggedOut.Task.WaitAsync(Guard);

            Assert.False(connection.IsAuthenticated);
            Assert.Equal(FakeGateway.NoToken, connection.Token);
            Assert.True(connection.Connected);
        }

        [Fact]
        public async Task AnsweredLogoutEndsTheAuthenticationWithoutRaisingLoggedOut()
        {
            using var gateway = new FakeGateway();
            using var connection = await ConnectAsync(gateway);
            await LoginAsync(connection);

            bool loggedOut = false;
            connection.LoggedOut += (sender, e) => loggedOut = true;

            Package response = await connection.SendAsync(Request(Command.Logout));

            // LoggedOut would have been raised before the response completed the request.
            Assert.Equal(Command.Logout, response.Message.Command);
            Assert.False(connection.IsAuthenticated);
            Assert.False(loggedOut);
        }

        [Fact]
        public async Task CommandThatNeedsALoginIsRefusedLocallyBeforeLogin()
        {
            using var gateway = new FakeGateway();
            using var connection = await ConnectAsync(gateway);

            await Assert.ThrowsAsync<InvalidOperationException>(() => connection.SendAsync(Request(Command.Jmcp, PayloadFactory.GetUsers())));

            // The ping arrives after anything the refused request could have sent.
            await connection.SendAsync(Ping());

            Assert.Equal(new[] { "PING" }, gateway.RequestNames);
        }

        [Fact]
        public async Task LoginStoresTheTokenAndLaterRequestsCarryIt()
        {
            using var gateway = new FakeGateway();
            using var connection = await ConnectAsync(gateway);

            await LoginAsync(connection);
            string firstToken = connection.Token;

            Assert.True(connection.IsAuthenticated);
            Assert.NotEqual(FakeGateway.NoToken, firstToken);
            Assert.Equal(gateway.SessionToken, firstToken);

            Package users = await connection.SendAsync(Request(Command.Jmcp, PayloadFactory.GetUsers()));
            Assert.Equal(Command.Jmcp, users.Message.Command);

            // A new login starts without a token and replaces the old one.
            await LoginAsync(connection);

            Assert.Equal(new[] { FakeGateway.NoToken, firstToken, FakeGateway.NoToken }, gateway.Requests.Select(r => r.Token));
            Assert.All(gateway.Requests, request => Assert.True(request.ChecksumsValid));
            Assert.NotEqual(firstToken, connection.Token);
            Assert.Equal(gateway.SessionToken, connection.Token);
        }

        [Fact]
        public async Task ResponseWithAForeignTokenIsIgnored()
        {
            using var gateway = new FakeGateway();
            using var connection = await ConnectAsync(gateway);
            await LoginAsync(connection);
            Channel<PackageReceivedEventArgs> events = Record(connection);
            connection.Timeouts.Set(Command.Ping, 300);

            gateway.AnswerNextWithToken(Command.Ping, "0BADF00D");

            await Assert.ThrowsAsync<TimeoutException>(() => connection.SendAsync(Ping()));
            Assert.False(events.Reader.TryRead(out _));

            // The same answer with the session's token is accepted.
            Package response = await connection.SendAsync(Ping());

            Assert.Equal(gateway.SessionToken, response.Message.Token);
            Assert.Same(response, (await NextAsync(events)).Package);
        }

        // A handler attached right after an awaited request must not see that request's package
        // any more, so the event has to be over before the request completes.
        [Fact]
        public async Task PackageReceivedIsRaisedBeforeTheRequestCompletes()
        {
            using var gateway = new FakeGateway();
            using var connection = await ConnectAsync(gateway);

            using var handlerMayReturn = new ManualResetEventSlim();
            connection.PackageReceived += (sender, e) => handlerMayReturn.Wait(Guard);

            Task<Package> request = connection.SendAsync(Ping());
            await gateway.WaitForRequestAsync(Command.Ping);
            await Task.Delay(200);
            Assert.False(request.IsCompleted);

            handlerMayReturn.Set();

            Assert.Equal(Command.Ping, (await request.WaitAsync(Guard)).Message.Command);
        }

        [Fact]
        public async Task LaterRequestsAreSentFromTheClientIdTheGatewayAssigned()
        {
            using var gateway = new FakeGateway();
            using var connection = await ConnectAsync(gateway);
            Assert.Equal("000000000000", connection.ClientId);

            await connection.SendAsync(Ping());
            Assert.Equal(FakeGateway.AssignedClientId, connection.ClientId);

            await connection.SendAsync(Ping());

            Assert.Equal(new[] { "000000000000", FakeGateway.AssignedClientId }, gateway.Requests.Select(r => r.Sender));
        }
    }
}
