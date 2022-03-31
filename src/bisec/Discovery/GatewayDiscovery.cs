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
                var xml = XElement.Parse(receiveString);

                state.Discovery._discoveryData = new DiscoveryResult()
                {
                    HwVersion = xml.Attribute("hwVersion").Value,
                    SwVersion = xml.Attribute("swVersion").Value,
                    Mac = xml.Attribute("mac").Value,
                    Protocol = xml.Attribute("protocol").Value,
                    SourceAddress = e.Address,
                };

                state.Discovery.MessageReceived = true;

                // Signal the main thread to continue.  
                discoveryCompleted.Set();
            }
            catch { }
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
            string message = "<Discover target=\"LogicBox\"/>";
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
