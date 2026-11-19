using BiSec.Library;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace tests
{
    public class GatewayDiscoveryTests
    {
        private const string MacA = "54:10:EC:85:2A:31";
        private const string MacB = "54:10:EC:85:2A:32";

        private static readonly TimeSpan ShortWindow = TimeSpan.FromMilliseconds(300);
        private static readonly TimeSpan LongWindow = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan HangGuard = TimeSpan.FromSeconds(10);

        [Fact]
        public async Task ReturnsEveryGatewayOnceInArrivalOrder()
        {
            // The later replies are delayed so they only count if listening goes on after the first one.
            using var gateway = new FakeGateway(Reply(MacA), Reply(MacB), Reply(MacA.ToLowerInvariant()))
            {
                ReplyInterval = TimeSpan.FromMilliseconds(100)
            };
            IPEndPoint listenEndPoint = FreeLoopbackEndPoint();
            var discovery = new GatewayDiscovery(listenEndPoint, new[] { gateway.EndPoint });

            var results = await discovery.DiscoverAsync(TimeSpan.FromMilliseconds(750)).WaitAsync(HangGuard);

            Assert.Equal(new[] { "5410EC852A31", "5410EC852A32" }, results.Select(r => r.GatewayId));
            Assert.All(results, r => Assert.Equal(IPAddress.Loopback, r.SourceAddress));

            var request = Assert.Single(gateway.Requests);
            Assert.Equal("<Discover target=\"LogicBox\" />", request.Text);
            Assert.Equal(listenEndPoint.Port, request.Source.Port);
        }

        [Fact]
        public async Task ReturnsEmptyListAfterTimeoutWhenNothingAnswers()
        {
            using var silent = new FakeGateway();
            var discovery = new GatewayDiscovery(FreeLoopbackEndPoint(), new[] { silent.EndPoint });

            var stopwatch = Stopwatch.StartNew();
            var results = await discovery.DiscoverAsync(ShortWindow).WaitAsync(HangGuard);
            stopwatch.Stop();

            Assert.Empty(results);
            Assert.Single(silent.Requests);
            Assert.True(stopwatch.Elapsed >= TimeSpan.FromMilliseconds(250), $"Returned after {stopwatch.Elapsed}.");
        }

        [Fact]
        public async Task CanRunRepeatedlyOnTheSameListenPort()
        {
            using var gateway = new FakeGateway(Reply(MacA));
            IPEndPoint listenEndPoint = FreeLoopbackEndPoint();
            var first = new GatewayDiscovery(listenEndPoint, new[] { gateway.EndPoint });
            var second = new GatewayDiscovery(listenEndPoint, new[] { gateway.EndPoint });

            foreach (var discovery in new[] { first, first, second })
            {
                var results = await discovery.DiscoverAsync(ShortWindow).WaitAsync(HangGuard);

                Assert.Equal("5410EC852A31", Assert.Single(results).GatewayId);
            }
        }

        [Fact]
        public async Task SkipsInvalidRepliesAndKeepsListening()
        {
            using var gateway = new FakeGateway("this is not xml", "<Discover protocol=\"3\" />", Reply(MacA));
            var discovery = new GatewayDiscovery(FreeLoopbackEndPoint(), new[] { gateway.EndPoint });

            var results = await discovery.DiscoverAsync(ShortWindow).WaitAsync(HangGuard);

            Assert.Equal("5410EC852A31", Assert.Single(results).GatewayId);
        }

        [Fact]
        public async Task FailedSendDoesNotAbortRemainingTargets()
        {
            using var gatewayA = new FakeGateway(Reply(MacA));
            using var gatewayB = new FakeGateway(Reply(MacB));
            var targets = new[] { UnsendableEndPoint, gatewayA.EndPoint, gatewayB.EndPoint };
            var discovery = new GatewayDiscovery(FreeLoopbackEndPoint(), targets);

            var results = await discovery.DiscoverAsync(ShortWindow).WaitAsync(HangGuard);

            Assert.Equal(new[] { "5410EC852A31", "5410EC852A32" }, results.Select(r => r.GatewayId).OrderBy(id => id));
        }

        [Fact]
        public async Task CancellationEndsTheWaitAndReleasesThePort()
        {
            using var gateway = new FakeGateway(Reply(MacA));
            IPEndPoint listenEndPoint = FreeLoopbackEndPoint();
            var discovery = new GatewayDiscovery(listenEndPoint, new[] { gateway.EndPoint });

            using (var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100)))
            {
                await Assert.ThrowsAnyAsync<OperationCanceledException>(
                    () => discovery.DiscoverAsync(LongWindow, cts.Token).WaitAsync(HangGuard));
            }

            Assert.Single(await discovery.DiscoverAsync(ShortWindow).WaitAsync(HangGuard));
        }

        [Fact]
        public async Task ThrowsAndReleasesThePortWhenNoRequestCanBeSent()
        {
            using var gateway = new FakeGateway(Reply(MacA));
            IPEndPoint listenEndPoint = FreeLoopbackEndPoint();
            var failing = new GatewayDiscovery(listenEndPoint, new[] { UnsendableEndPoint });

            await Assert.ThrowsAnyAsync<SocketException>(() => failing.DiscoverAsync(LongWindow).WaitAsync(HangGuard));

            var working = new GatewayDiscovery(listenEndPoint, new[] { gateway.EndPoint });
            Assert.Single(await working.DiscoverAsync(ShortWindow).WaitAsync(HangGuard));
        }

        [Fact]
        public async Task DiscoverReturnsFirstGatewayWithoutWaitingForTheWindow()
        {
            using var gateway = new FakeGateway(Reply(MacB), Reply(MacA));
            var discovery = new GatewayDiscovery(FreeLoopbackEndPoint(), new[] { gateway.EndPoint });

            var result = await Task.Run(() => discovery.Discover(LongWindow)).WaitAsync(HangGuard);

            Assert.Equal("5410EC852A32", result.GatewayId);
        }

        [Fact]
        public void DiscoverReturnsNullWhenNothingAnswers()
        {
            using var silent = new FakeGateway();
            var discovery = new GatewayDiscovery(FreeLoopbackEndPoint(), new[] { silent.EndPoint });

            Assert.Null(discovery.Discover(ShortWindow));
        }

        [Fact]
        public void ParsesAttributeDiscoveryResponse()
        {
            var result = GatewayDiscovery.ParseDiscoveryResponse(
                "<Discover hwVersion=\"1\" swVersion=\"2\" mac=\"54:10:EC:85:2A:31\" protocol=\"3\" />",
                IPAddress.Parse("192.168.1.10"));

            Assert.Equal("5410EC852A31", result.GatewayId);
            Assert.Equal("3", result.Protocol);
            Assert.Equal("1", result.HwVersion);
            Assert.Equal("2", result.SwVersion);
        }

        [Fact]
        public void ParsesEdgeDiscoveryResponse()
        {
            var result = GatewayDiscovery.ParseDiscoveryResponse(
                "<Root><Edge><Mac>54:10:EC:85:2A:31</Mac><ProtocolVersion>4</ProtocolVersion></Edge></Root>",
                IPAddress.Parse("192.168.1.10"));

            Assert.Equal("5410EC852A31", result.GatewayId);
            Assert.Equal("4", result.Protocol);
        }

        // Sending a datagram to port 0 is rejected by the operating system.
        private static IPEndPoint UnsendableEndPoint => new IPEndPoint(IPAddress.Loopback, 0);

        private static string Reply(string mac)
        {
            return $"<Discover hwVersion=\"1\" swVersion=\"2\" mac=\"{mac}\" protocol=\"3\" />";
        }

        private static IPEndPoint FreeLoopbackEndPoint()
        {
            using (var probe = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
                return (IPEndPoint)probe.Client.LocalEndPoint;
        }

        /// <summary>
        /// Answers every datagram it receives with the configured replies, sent to the sender's address and port.
        /// </summary>
        private sealed class FakeGateway : IDisposable
        {
            private readonly UdpClient _client = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            private readonly CancellationTokenSource _stop = new CancellationTokenSource();
            private readonly string[] _replies;
            private readonly Task _loop;

            public FakeGateway(params string[] replies)
            {
                _replies = replies;
                _loop = Task.Run(RunAsync);
            }

            public IPEndPoint EndPoint => (IPEndPoint)_client.Client.LocalEndPoint;

            public TimeSpan ReplyInterval { get; set; }

            public ConcurrentQueue<(string Text, IPEndPoint Source)> Requests { get; } = new ConcurrentQueue<(string Text, IPEndPoint Source)>();

            private async Task RunAsync()
            {
                while (!_stop.IsCancellationRequested)
                {
                    try
                    {
                        UdpReceiveResult request = await _client.ReceiveAsync(_stop.Token);
                        Requests.Enqueue((Encoding.ASCII.GetString(request.Buffer), request.RemoteEndPoint));

                        for (int i = 0; i < _replies.Length; i++)
                        {
                            if (i > 0 && ReplyInterval > TimeSpan.Zero)
                                await Task.Delay(ReplyInterval, _stop.Token);

                            await _client.SendAsync(Encoding.ASCII.GetBytes(_replies[i]), request.RemoteEndPoint, _stop.Token);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                    }
                    catch (SocketException)
                    {
                        // The discovery socket may already be closed when a late reply is sent.
                    }
                }
            }

            public void Dispose()
            {
                _stop.Cancel();
                _loop.Wait(TimeSpan.FromSeconds(5));
                _client.Dispose();
                _stop.Dispose();
            }
        }
    }
}
