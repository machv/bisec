using BiSec.Library;
using BiSec.Library.Exceptions;
using BiSec.Library.Models;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace tests
{
    /// <summary>
    /// Gateway name, firmware version and WiFi.
    /// </summary>
    public class GatewayClientGatewayTests
    {
        [Fact]
        public async Task GetNameWorksWithoutALoginAndDecodesUtf8()
        {
            using var session = new GatewayTestSession();
            session.Gateway.Name = "Garage S\u00FCd";

            string name = await session.Client.GetNameAsync();

            Assert.Equal("Garage S\u00FCd", name);
            Assert.Equal(FakeGateway.NoToken, Assert.Single(session.Gateway.Requests).Token);
        }

        [Fact]
        public async Task SetNameSendsTheNameAsUtf8()
        {
            using var session = await GatewayTestSession.AsAdminAsync();

            await session.Client.SetNameAsync("Garage S\u00FCd");

            FakeRequest request = Assert.Single(session.Gateway.Requests);
            Assert.Equal(Command.SetName, request.Command);
            Assert.Equal(Encoding.UTF8.GetBytes("Garage S\u00FCd"), request.Payload);
            Assert.Equal("Garage S\u00FCd", session.Gateway.Name);
        }

        [Fact]
        public async Task GatewayThatDoesNotKnowTheVersionCommandIsTreatedAsFirmware06OrLower()
        {
            using var session = new GatewayTestSession();
            session.Gateway.FirmwareVersion = null;

            GatewayVersion version = await session.Client.GetFirmwareVersionAsync();

            Assert.Same(GatewayVersion.Ee001425_06OrLower, version);

            // The plain call does not hide the error.
            GatewayException e = await Assert.ThrowsAsync<GatewayException>(() => session.Client.GetGatewayVersionAsync());
            Assert.Equal(Error.CommandNotFound, e.Error);
        }

        [Fact]
        public async Task FirmwareVersionIsParsedAndRememberedUntilRefreshed()
        {
            using var session = new GatewayTestSession();
            session.Gateway.FirmwareVersion = "EE001425-10";

            GatewayVersion version = await session.Client.GetFirmwareVersionAsync();

            Assert.Equal("EE001425-10", version.Name);
            Assert.Equal(10, version.Number);

            session.Gateway.FirmwareVersion = "EE001425-12";
            GatewayVersion remembered = await session.Client.GetFirmwareVersionAsync();
            GatewayVersion refreshed = await session.Client.GetFirmwareVersionAsync(refresh: true);

            Assert.Equal(10, remembered.Number);
            Assert.Equal(12, refreshed.Number);
            Assert.Equal(new[] { "GET_GW_VERSION", "GET_GW_VERSION" }, session.Gateway.RequestNames);
        }

        [Theory]
        [InlineData(0, WifiState.Connected)]
        [InlineData(1, WifiState.NotConnected)]
        [InlineData(64, WifiState.Busy)]
        [InlineData(128, WifiState.ApNotFound)]
        [InlineData(130, WifiState.AuthenticationFailure)]
        public async Task WifiStateByteIsMapped(int state, WifiState expected)
        {
            using var session = await GatewayTestSession.AsAdminAsync();
            session.Gateway.WifiState = (byte)state;

            Assert.Equal(expected, await session.Client.GetWifiStateAsync());
        }

        [Theory]
        [InlineData(false)]
        // WIFI_FOUND must not be taken for the answer even when it repeats the request's tag.
        [InlineData(true)]
        public async Task ScanWifiReturnsThePushedNetworks(bool wifiFoundUsesRequestTag)
        {
            using var session = await GatewayTestSession.AsAdminAsync();
            session.Gateway.WifiFoundUsesRequestTag = wifiFoundUsesRequestTag;
            session.Gateway.WifiNetworks.AddRange(new[] { "Home", "Caf\u00E9 5G", "Guest" });

            IReadOnlyList<string> networks = await session.Client.ScanWifiAsync();

            Assert.Equal(new[] { "Home", "Caf\u00E9 5G", "Guest" }, networks);
            Assert.Equal(new[] { "SCAN_WIFI" }, session.Gateway.RequestNames);
        }

        [Fact]
        public async Task ScanWifiOnlyReturnsNetworksReportedDuringTheScan()
        {
            using var session = await GatewayTestSession.AsAdminAsync();
            session.Gateway.WifiNetworks.Add("Home");

            await session.Gateway.PushAsync(Command.WifiFound, Encoding.UTF8.GetBytes("\u0001Stray"));
            // The answer to the ping arrives after the stray package.
            await session.Client.PingAsync();

            IReadOnlyList<string> networks = await session.Client.ScanWifiAsync();

            Assert.Equal(new[] { "Home" }, networks);
        }

        [Fact]
        public async Task ScanWifiOnABusyGatewayThrowsGatewayBusy()
        {
            using var session = await GatewayTestSession.AsAdminAsync();
            session.Gateway.FailNext(Command.ScanWifi, Error.GatewayBusy);

            GatewayException e = await Assert.ThrowsAsync<GatewayException>(() => session.Client.ScanWifiAsync());

            Assert.Equal(Error.GatewayBusy, e.Error);
            Assert.Equal(Command.ScanWifi, e.Command);
        }
    }
}
