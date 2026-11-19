using BiSec.Library;
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace tests
{
    public class DiscoveryNameResolverTests
    {
        private static readonly TimeSpan Guard = TimeSpan.FromSeconds(10);

        private static DiscoveryResult Discovered()
        {
            return new DiscoveryResult
            {
                Mac = "54:10:EC:85:2A:31",
                SourceAddress = IPAddress.Loopback,
            };
        }

        [Fact]
        public async Task ResolvesTheNameWithoutLoggingIn()
        {
            using var gateway = new FakeGateway { Name = "Carport" };
            DiscoveryResult discovered = Discovered();

            string name = await DiscoveryNameResolver.ResolveNameAsync(discovered, gateway.Port, null, CancellationToken.None).WaitAsync(Guard);

            Assert.Equal("Carport", name);
            Assert.Equal("Carport", discovered.Name);

            FakeRequest request = Assert.Single(gateway.Requests);
            Assert.Equal(Command.GetName, request.Command);
            Assert.Equal(FakeGateway.GatewayId, request.Receiver);
            Assert.Equal(FakeGateway.NoToken, request.Token);
        }

        [Fact]
        public async Task ReturnsNullForAnUnreachableGateway()
        {
            using var refusing = new RefusingPort();
            DiscoveryResult discovered = Discovered();

            string name = await DiscoveryNameResolver.ResolveNameAsync(discovered, refusing.Port, null, CancellationToken.None).WaitAsync(Guard);

            Assert.Null(name);
            Assert.Null(discovered.Name);
        }

        [Fact]
        public async Task ReturnsNullWhenTheGatewayDropsTheConnection()
        {
            using var gateway = new FakeGateway();
            gateway.IgnoreNext(Command.GetName);
            DiscoveryResult discovered = Discovered();

            Task<string> resolving = DiscoveryNameResolver.ResolveNameAsync(discovered, gateway.Port, null, CancellationToken.None);
            await gateway.WaitForRequestAsync(Command.GetName);
            gateway.DropConnection();

            Assert.Null(await resolving.WaitAsync(Guard));
            Assert.Null(discovered.Name);
        }

        [Fact]
        public async Task ReturnsNullWhenTheGatewayAnswersWithAnError()
        {
            using var gateway = new FakeGateway();
            gateway.FailNext(Command.GetName, Error.GatewayBusy);
            DiscoveryResult discovered = Discovered();

            string name = await DiscoveryNameResolver.ResolveNameAsync(discovered, gateway.Port, null, CancellationToken.None).WaitAsync(Guard);

            Assert.Null(name);
            Assert.Null(discovered.Name);
        }
    }
}
