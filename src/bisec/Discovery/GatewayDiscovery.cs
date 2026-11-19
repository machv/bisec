using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;

namespace BiSec.Library
{
    public class GatewayDiscovery
    {
        public const int RequestPort = 4001;
        public const int ListenPort = 4002;

        public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(3);

        private const string DiscoveryRequest = "<Discover target=\"LogicBox\" />";
        private const int MaxDatagramSize = 65535;

        private readonly IPEndPoint _listenEndPoint;
        private readonly IPEndPoint[] _requestTargets;
        private readonly ILogger _logger;

        public GatewayDiscovery() : this((ILogger)null) { }

        public GatewayDiscovery(ILogger logger) : this(new IPEndPoint(IPAddress.Any, ListenPort), null, logger) { }

        // Lets tests run on loopback with their own ports. Null requestTargets selects the default broadcast targets.
        internal GatewayDiscovery(IPEndPoint listenEndPoint, IEnumerable<IPEndPoint> requestTargets, ILogger logger = null)
        {
            _listenEndPoint = listenEndPoint ?? throw new ArgumentNullException(nameof(listenEndPoint));
            _requestTargets = requestTargets?.ToArray();
            _logger = logger;
        }

        /// <summary>
        /// Broadcasts a discovery request and collects the answers for the whole <paramref name="timeout"/>
        /// (<see cref="DefaultTimeout"/> when null).
        /// </summary>
        /// <returns>Every gateway that answered, once per MAC address, in order of arrival.</returns>
        /// <exception cref="SocketException">The listen port is in use or no request could be sent.</exception>
        /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
        public async Task<IReadOnlyList<DiscoveryResult>> DiscoverAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        {
            return await DiscoverCoreAsync(timeout, false, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Blocks until the first gateway answers and returns it, or null when none answers within <paramref name="timeout"/>.
        /// </summary>
        public DiscoveryResult Discover(TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        {
            return DiscoverCoreAsync(timeout, true, cancellationToken).GetAwaiter().GetResult().FirstOrDefault();
        }

        private async Task<List<DiscoveryResult>> DiscoverCoreAsync(TimeSpan? timeout, bool stopAtFirst, CancellationToken cancellationToken)
        {
            TimeSpan window = timeout ?? DefaultTimeout;
            if (window < TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(timeout), "Discovery timeout must not be negative.");

            cancellationToken.ThrowIfCancellationRequested();

            var results = new List<DiscoveryResult>();
            var knownGateways = new HashSet<string>(StringComparer.Ordinal);

            using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
            using (var windowCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                socket.EnableBroadcast = true;
                socket.Bind(_listenEndPoint);

                await SendDiscoveryRequestAsync(socket, cancellationToken).ConfigureAwait(false);

                windowCts.CancelAfter(window);

                byte[] buffer = new byte[MaxDatagramSize];
                EndPoint anySource = new IPEndPoint(IPAddress.Any, 0);

                try
                {
                    while (true)
                    {
                        SocketReceiveFromResult received;
                        try
                        {
                            received = await socket.ReceiveFromAsync(buffer.AsMemory(), SocketFlags.None, anySource, windowCts.Token).ConfigureAwait(false);
                        }
                        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionReset)
                        {
                            // Windows reports an ICMP "port unreachable" for an earlier send on the next receive.
                            continue;
                        }

                        IPAddress sourceAddress = ((IPEndPoint)received.RemoteEndPoint).Address;
                        string response = Encoding.UTF8.GetString(buffer, 0, received.ReceivedBytes);

                        DiscoveryResult result;
                        try
                        {
                            result = ParseDiscoveryResponse(response, sourceAddress);
                        }
                        catch (Exception ex) when (ex is XmlException || ex is FormatException)
                        {
                            _logger?.LogWarning($"Ignoring invalid discovery response from {sourceAddress} ({ex.Message}): {response}");
                            continue;
                        }

                        if (!knownGateways.Add(result.GatewayId))
                            continue;

                        results.Add(result);

                        if (stopAtFirst)
                            break;
                    }
                }
                catch (OperationCanceledException)
                {
                    // Only the end of the listening window is expected here.
                    cancellationToken.ThrowIfCancellationRequested();
                }
            }

            return results;
        }

        private async Task SendDiscoveryRequestAsync(Socket socket, CancellationToken cancellationToken)
        {
            byte[] data = Encoding.ASCII.GetBytes(DiscoveryRequest);

            bool sent = false;
            SocketException lastError = null;

            foreach (IPEndPoint target in _requestTargets ?? GetBroadcastTargets())
            {
                try
                {
                    await socket.SendToAsync(data.AsMemory(), SocketFlags.None, target, cancellationToken).ConfigureAwait(false);
                    sent = true;
                }
                catch (SocketException ex)
                {
                    lastError = ex;
                    _logger?.LogWarning($"Sending discovery request to {target} failed: {ex.Message}");
                }
            }

            if (!sent && lastError != null)
                ExceptionDispatchInfo.Throw(lastError);
        }

        private IPEndPoint[] GetBroadcastTargets()
        {
            var addresses = new List<IPAddress>();

            try
            {
                addresses.AddRange(NetworkHelper.GetInterfaceBroadcasts());
            }
            catch (NetworkInformationException ex)
            {
                _logger?.LogWarning($"Reading the network interfaces failed ({ex.Message}); using the limited broadcast address only.");
            }

            // The limited broadcast is what the BiSecur app sends; the directed ones reach interfaces it is not routed to.
            addresses.Add(IPAddress.Broadcast);

            return addresses.Distinct().Select(address => new IPEndPoint(address, RequestPort)).ToArray();
        }

        internal static DiscoveryResult ParseDiscoveryResponse(string response, IPAddress sourceAddress)
        {
            var xml = XElement.Parse(response);

            string mac = GetValue(xml, "mac");
            string protocol = GetValue(xml, "protocol") ?? GetValue(xml, "protocolVersion");
            string hwVersion = GetValue(xml, "hwVersion");
            string swVersion = GetValue(xml, "swVersion");

            if (string.IsNullOrWhiteSpace(mac))
                mac = FindMacLikeValue(xml);

            if (string.IsNullOrWhiteSpace(mac))
                throw new FormatException("Discovery response does not contain a gateway MAC address.");

            return new DiscoveryResult()
            {
                HwVersion = hwVersion,
                SwVersion = swVersion,
                Mac = mac,
                Protocol = protocol,
                SourceAddress = sourceAddress,
            };
        }

        private static string GetValue(XElement xml, string name)
        {
            foreach (var element in xml.DescendantsAndSelf())
            {
                foreach (var attribute in element.Attributes())
                {
                    if (string.Equals(attribute.Name.LocalName, name, StringComparison.OrdinalIgnoreCase))
                        return attribute.Value;
                }

                if (string.Equals(element.Name.LocalName, name, StringComparison.OrdinalIgnoreCase))
                    return element.Value;
            }

            return null;
        }

        private static string FindMacLikeValue(XElement xml)
        {
            foreach (var element in xml.DescendantsAndSelf())
            {
                string value = element.Value?.Trim();
                if (!string.IsNullOrWhiteSpace(value) && value.Split(':').Length == 6)
                    return value;
            }

            return null;
        }
    }
}
