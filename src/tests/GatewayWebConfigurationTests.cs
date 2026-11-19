using BiSec.Library;
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace tests
{
    public class GatewayWebConfigurationTests
    {
        // ASSUMPTION: the HTML of the gateway's real index.htm is not available. This sample is only
        // shaped after the two regular expressions the legacy app applies to the page
        // (WifiSettingsScreen.SSID_SCAN_REGEX and PASSPHRASE_SCAN_REGEX): an input named "Ssid" or "pw"
        // whose value attribute follows directly. The tests therefore show that the parser agrees
        // with those expressions, not that it reads a real gateway's page.
        private const string SamplePage =
            "<html><body><form method=\"post\" action=\"index.htm\">\n" +
            "<input type=\"text\" name=\"Ssid\" value=\"Home Net\">\n" +
            "<select name=\"wsec\"><option value=\"WPAAuto\">WPA/WPA2</option></select>\n" +
            "<input type=\"password\" name=\"pw\" value=\"s3cret pass\">\n" +
            "</form></body></html>";

        private sealed class StubHandler : HttpMessageHandler
        {
            public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
            public string Page { get; set; } = "";

            public HttpMethod Method { get; private set; }
            public Uri Uri { get; private set; }
            public string Body { get; private set; }
            public string ContentType { get; private set; }

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Method = request.Method;
                Uri = request.RequestUri;

                if (request.Content != null)
                {
                    Body = await request.Content.ReadAsStringAsync(cancellationToken);
                    ContentType = request.Content.Headers.ContentType?.MediaType;
                }

                return new HttpResponseMessage(Status) { Content = new StringContent(Page) };
            }
        }

        private static (string Name, string Value)[] ParseForm(string body)
        {
            return body.Split('&')
                .Select(field => field.Split('='))
                .Select(parts => (Uri.UnescapeDataString(parts[0]), Uri.UnescapeDataString(parts[1].Replace('+', ' '))))
                .ToArray();
        }

        [Fact]
        public void ParseWifiSettingsReadsSsidAndPassphraseFromTheSamplePage()
        {
            GatewayWifiSettings settings = GatewayWebConfiguration.ParseWifiSettings(SamplePage);

            Assert.Equal("Home Net", settings.Ssid);
            Assert.Equal("s3cret pass", settings.Passphrase);
        }

        [Fact]
        public void ParseWifiSettingsGivesNullForAFieldThePageDoesNotContain()
        {
            GatewayWifiSettings settings = GatewayWebConfiguration.ParseWifiSettings(
                "<input type=\"text\" name=\"Ssid\" value=\"\"><p>no passphrase field</p>");

            Assert.Equal("", settings.Ssid);
            Assert.Null(settings.Passphrase);
        }

        [Fact]
        public async Task ReadWifiSettingsGetsTheIndexPageOfTheGateway()
        {
            var handler = new StubHandler { Page = SamplePage };
            using var httpClient = new HttpClient(handler);
            var configuration = new GatewayWebConfiguration(IPAddress.Parse("192.168.1.20"), httpClient);

            GatewayWifiSettings settings = await configuration.ReadWifiSettingsAsync();

            Assert.Equal(HttpMethod.Get, handler.Method);
            Assert.Equal(new Uri("http://192.168.1.20/index.htm"), handler.Uri);
            Assert.Equal("Home Net", settings.Ssid);
            Assert.Equal("s3cret pass", settings.Passphrase);
        }

        [Fact]
        public async Task WriteWifiSettingsPostsSsidSecurityAndPassphraseAsFormFields()
        {
            var handler = new StubHandler();
            using var httpClient = new HttpClient(handler);
            var configuration = new GatewayWebConfiguration(new Uri("http://gateway.test/index.htm"), httpClient);

            await configuration.WriteWifiSettingsAsync("Home Net", "p&ss=w\u00F6rd");

            Assert.Equal(HttpMethod.Post, handler.Method);
            Assert.Equal(new Uri("http://gateway.test/index.htm"), handler.Uri);
            Assert.Equal("application/x-www-form-urlencoded", handler.ContentType);
            Assert.Equal(
                new[] { ("Ssid", "Home Net"), ("wsec", "WPAAuto"), ("pw", "p&ss=w\u00F6rd") },
                ParseForm(handler.Body));
        }

        [Fact]
        public async Task WriteWifiSettingsFailsWhenTheGatewayRejectsThePost()
        {
            var handler = new StubHandler { Status = HttpStatusCode.InternalServerError };
            using var httpClient = new HttpClient(handler);
            var configuration = new GatewayWebConfiguration(new Uri("http://gateway.test/index.htm"), httpClient);

            await Assert.ThrowsAsync<HttpRequestException>(() => configuration.WriteWifiSettingsAsync("Home Net", "secret"));
        }
    }
}
