using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace BiSec.Library
{
    public class GatewayWifiSettings
    {
        public string Ssid { get; set; }
        public string Passphrase { get; set; }
    }

    /// <summary>
    /// Reads and writes the gateway's WiFi credentials the way the legacy app does: not through the
    /// MCP protocol but through the gateway's own web page, http://&lt;gateway&gt;/index.htm.
    /// This is unencrypted HTTP; the passphrase travels and is returned in clear text.
    /// </summary>
    public class GatewayWebConfiguration
    {
        // WifiSettingsScreen.SSID_SCAN_REGEX / PASSPHRASE_SCAN_REGEX
        private static readonly Regex SsidPattern = new Regex("(?<=Ssid.\\svalue.{2}).*?(?=\")");
        private static readonly Regex PassphrasePattern = new Regex("(?<=pw.\\svalue.{2}).*?(?=\")");

        private readonly HttpClient _httpClient;
        private readonly Uri _pageUri;

        public GatewayWebConfiguration(IPAddress gatewayAddress, HttpClient httpClient = null)
            : this(new Uri($"http://{gatewayAddress ?? throw new ArgumentNullException(nameof(gatewayAddress))}/index.htm"), httpClient)
        {
        }

        internal GatewayWebConfiguration(Uri pageUri, HttpClient httpClient)
        {
            _pageUri = pageUri;
            _httpClient = httpClient ?? new HttpClient();
        }

        /// <summary>
        /// Reads the stored SSID and passphrase from the page. A value the page does not contain is null.
        /// </summary>
        public async Task<GatewayWifiSettings> ReadWifiSettingsAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            string page = await _httpClient.GetStringAsync(_pageUri, cancellationToken).ConfigureAwait(false);

            return ParseWifiSettings(page);
        }

        /// <summary>
        /// Stores new WiFi credentials. The gateway then tries to join that network;
        /// follow the outcome with <see cref="GatewayClient.GetWifiStateAsync"/>.
        /// </summary>
        public async Task WriteWifiSettingsAsync(string ssid, string passphrase, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (ssid == null)
                throw new ArgumentNullException(nameof(ssid));

            if (passphrase == null)
                throw new ArgumentNullException(nameof(passphrase));

            using (FormUrlEncodedContent content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("Ssid", ssid),
                new KeyValuePair<string, string>("wsec", "WPAAuto"),
                new KeyValuePair<string, string>("pw", passphrase),
            }))
            using (HttpResponseMessage response = await _httpClient.PostAsync(_pageUri, content, cancellationToken).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
            }
        }

        internal static GatewayWifiSettings ParseWifiSettings(string page)
        {
            Match ssid = SsidPattern.Match(page ?? string.Empty);
            Match passphrase = PassphrasePattern.Match(page ?? string.Empty);

            return new GatewayWifiSettings
            {
                Ssid = ssid.Success ? WebUtility.HtmlDecode(ssid.Value) : null,
                Passphrase = passphrase.Success ? WebUtility.HtmlDecode(passphrase.Value) : null,
            };
        }
    }
}
