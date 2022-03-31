using System;
using System.Net;
using System.Net.NetworkInformation;

namespace BiSec.Library
{
    public static class UnicastIPAddressInformationExtensions
    {
        public static IPAddress GetInterfaceBroadcastAddress(this UnicastIPAddressInformation unicastAddress)
        {
            return GetInterfaceBroadcastAddress(unicastAddress.Address, unicastAddress.IPv4Mask);
        }

        public static IPAddress GetInterfaceBroadcastAddress(IPAddress address, IPAddress mask)
        {
            uint ipAddress = BitConverter.ToUInt32(address.GetAddressBytes(), 0);
            uint ipMaskV4 = BitConverter.ToUInt32(mask.GetAddressBytes(), 0);
            uint broadCastIpAddress = ipAddress | ~ipMaskV4;

            return new IPAddress(BitConverter.GetBytes(broadCastIpAddress));
        }
    }
}
