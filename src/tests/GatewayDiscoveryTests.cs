using BiSec.Library;
using System.Net;
using Xunit;

namespace tests
{
    public class GatewayDiscoveryTests
    {
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
    }
}
