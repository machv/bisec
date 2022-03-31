using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace BiSec.Library
{
    public class NetworkHelper
    {
        public static IEnumerable<IPAddress> GetInterfaceBroadcasts()
        {
            List<IPAddress> broadcasts = new List<IPAddress>();

            var ifaces = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces();
            foreach (var iface in ifaces)
            {
                if (iface.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up)
                    continue;

                var ipProperties = iface.GetIPProperties();
                var ipAddresses = ipProperties.UnicastAddresses;

                foreach (var ipAddress in ipAddresses)
                {
                    if (ipAddress.Address.AddressFamily != AddressFamily.InterNetwork)
                        continue;

                    IPAddress broadcastAddress = ipAddress.GetInterfaceBroadcastAddress();
                    broadcasts.Add(broadcastAddress);
                }
            }

            return broadcasts;
        }
    }
}
