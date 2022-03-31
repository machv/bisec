using System.Net;

namespace BiSec.Library
{
    public class DiscoveryResult
    {
        public string Mac { get; set; }
        public IPAddress SourceAddress { get; set; }
        public string SwVersion { get; set; }
        public string HwVersion { get; set; }
        public string Protocol { get; set; }

        public string GatewayId
        {
            get => Mac.Replace(":", "").ToUpper();
        }

        public override string ToString()
        {
            return $"Mac: {Mac}, SourceAddress: {SourceAddress}, SwVersion: {SwVersion}, HwVersion: {HwVersion}";
        }
    }
}
