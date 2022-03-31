using BiSec.Library.Exceptions;
using System.Text;

namespace BiSec.Library
{
    public class StringHelper
    {
        public static string HexStringFromByteArray(byte[] bytes, string delimiter = "")
        {
            var sb = new StringBuilder();
            foreach (var b in bytes)
                sb.AppendFormat("{0:X2}{1}", b, delimiter);

            return sb.ToString();
        }

        //https://stackoverflow.com/questions/321370/how-can-i-convert-a-hex-string-to-a-byte-array/9995303#9995303
        public static byte[] HexStringToByteArray(string hexString)
        {
            if (hexString.Length % 2 == 1)
                throw new InvalidPackageLengthException("The hex string cannot have an odd length.");

            byte[] output = new byte[hexString.Length >> 1];
            for (int i = 0; i < hexString.Length >> 1; ++i)
                output[i] = (byte)((GetHexValue(hexString[i << 1]) << 4) + (GetHexValue(hexString[(i << 1) + 1])));

            return output;
        }

        internal static int GetHexValue(int value)
        {
            // int val = hex;
            //For uppercase A-F letters:
            //return val - (val < 58 ? 48 : 55);
            //For lowercase a-f letters:
            //return val - (val < 58 ? 48 : 87);
            //Or the two combined, but a bit slower:
            return value - (value < 58 ? 48 : (value < 97 ? 55 : 87));
        }

        public static int ToHexInt(string hex)
        {
            var bytes = HexStringToByteArray(hex);
            int intBits = bytes[0] << 24 | (bytes[1] & 255) << 16 | (bytes[2] & 255) << 8 | bytes[3] & 255;
            return intBits;
        }
    }
}
