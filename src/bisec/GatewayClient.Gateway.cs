using BiSec.Library.Exceptions;
using BiSec.Library.Models;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BiSec.Library
{
    public partial class GatewayClient
    {
        public async Task<string> GetNameAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            Message response = await RequestAsync(Command.GetName, PayloadFactory.Empty, cancellationToken).ConfigureAwait(false);

            return response.Payload.TextContent;
        }

        public async Task SetNameAsync(string name, CancellationToken cancellationToken = default(CancellationToken))
        {
            await RequestAsync(Command.SetName, PayloadFactory.SetName(name), cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Returns the firmware version text, for example "EE001425-10". Firmware up to EE001425-06
        /// does not know the command and answers <see cref="Error.CommandNotFound"/>.
        /// </summary>
        public async Task<string> GetGatewayVersionAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            Message response = await RequestAsync(Command.GetGatewayVersion, PayloadFactory.Empty, cancellationToken).ConfigureAwait(false);

            return response.Payload.TextContent;
        }

        /// <summary>
        /// Returns the firmware version, treating a gateway that does not know the command as
        /// EE001425-06 or lower, like the app does.
        /// </summary>
        public async Task<GatewayVersion> GetFirmwareVersionAsync(bool refresh = false, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (_version != null && !refresh)
                return _version;

            try
            {
                string name = await GetGatewayVersionAsync(cancellationToken).ConfigureAwait(false);
                _version = GatewayVersion.FromName(name);
            }
            catch (GatewayException e) when (e.Error == Error.CommandNotFound)
            {
                _version = GatewayVersion.Ee001425_06OrLower;
            }

            return _version;
        }

        public async Task<WifiState> GetWifiStateAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            Message response = await RequestAsync(Command.GetWifiState, PayloadFactory.Empty, cancellationToken).ConfigureAwait(false);

            return (WifiState)RequirePayload(response, Command.GetWifiState, 1)[0];
        }

        /// <summary>
        /// Asks the gateway to scan for WiFi networks and returns the SSIDs it reports.
        /// Throws <see cref="GatewayException"/> with <see cref="Error.GatewayBusy"/> when a scan is already running.
        /// </summary>
        public async Task<IReadOnlyList<string>> ScanWifiAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            List<string> found = new List<string>();

            // The gateway reports each network in its own WIFI_FOUND package before it answers SCAN_WIFI.
            EventHandler<PackageReceivedEventArgs> handler = (sender, e) =>
            {
                Message message = e.Package.Message;
                if (message.Command.Code != Command.WifiFound.Code)
                    return;

                // WIFI_FOUND payload: [byte][ssid]; the app reads and ignores the first byte.
                byte[] data = message.Payload.ToByteArray();
                if (data.Length < 1)
                    return;

                lock (found)
                    found.Add(Encoding.UTF8.GetString(data, 1, data.Length - 1));
            };

            _gatewayConnection.PackageReceived += handler;
            try
            {
                await RequestAsync(Command.ScanWifi, PayloadFactory.Empty, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _gatewayConnection.PackageReceived -= handler;
            }

            lock (found)
                return found.ToArray();
        }
    }
}
