using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Xml.Linq;

namespace BiSec.Library
{
    public class GatewayDiscovery
    {
        private class DiscoveryState
        {
            private UdpClient _udpClient;
            private IPEndPoint _ipEndpoint;
            private GatewayDiscovery _discovery;

            public UdpClient UdpClient
            {
                get => _udpClient;
                set => _udpClient = value;
            }
            public IPEndPoint IPEndPoint
            {
                get => _ipEndpoint;
                set => _ipEndpoint = value;
            }
            public GatewayDiscovery Discovery
            {
                get => _discovery;
                set => _discovery = value;
            }
        }

        public static ManualResetEvent discoveryCompleted = new ManualResetEvent(false);

        private UdpClient _udpClient;
        private DiscoveryResult _discoveryData;

        private const int _listenPort = 4002;
        private bool _messageReceived = false;

        public bool MessageReceived
        {
            get => _messageReceived;
            internal set => _messageReceived = value;
        }

        public DiscoveryResult DiscoveryData
        {
            internal set
            {
                _discoveryData = value;
            }
            get { return _discoveryData; }
        }

        public GatewayDiscovery()
        {
            _udpClient = new UdpClient();
            _udpClient.EnableBroadcast = true;
        }

        private static void ReceiveCallback(IAsyncResult ar)
        {
            DiscoveryState state = (DiscoveryState)ar.AsyncState;

            UdpClient u = state.UdpClient;
            IPEndPoint e = state.IPEndPoint;

            byte[] receiveBytes = u.EndReceive(ar, ref e);
            string receiveString = Encoding.ASCII.GetString(receiveBytes);

            //Console.WriteLine($"Received: {receiveString}");
            try
            {
                state.Discovery._discoveryData = ParseDiscoveryResponse(receiveString, e.Address);

                state.Discovery.MessageReceived = true;

                // Signal the main thread to continue.  
                discoveryCompleted.Set();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Received invalid discovery response: {ex.Message}");
            }
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

        public void StartListener()
        {
            IPEndPoint e = new IPEndPoint(IPAddress.Any, _listenPort);
            UdpClient u = new UdpClient(e);

            DiscoveryState s = new DiscoveryState
            {
                IPEndPoint = e,
                UdpClient = u,
                Discovery = this
            };

            u.BeginReceive(new AsyncCallback(ReceiveCallback), s);
        }

        protected void SendDiscoveryRequest()
        {
            string message = "<Discover target=\"LogicBox\" />";
            byte[] data = Encoding.ASCII.GetBytes(message);

            Socket s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            s.EnableBroadcast = true;

            var broadcasts = NetworkHelper.GetInterfaceBroadcasts();
            foreach (var broadcastIp in broadcasts)
            {
                IPEndPoint endpoint = new IPEndPoint(broadcastIp, 4001);
                s.SendTo(data, endpoint);
            }

            s.Close();
        }

        public DiscoveryResult Discover()
        {
            // Set the event to nonsignaled state.  
            discoveryCompleted.Reset();

            StartListener();
            SendDiscoveryRequest();

            // Wait until a discovery is completed before continuing.  
            discoveryCompleted.WaitOne();

            return _discoveryData;
        }
    }
}
