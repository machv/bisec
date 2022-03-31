using System;
using System.Collections.Generic;
using System.Text;

namespace BiSec.Library
{
    public static class StringExtensions
    {
        public static byte[] ToByteArray(this string s)
        {
            return Encoding.UTF8.GetBytes(s);
        }

        public static byte[] ToHexByteArray(this string s)
        {
            return StringHelper.HexStringToByteArray(s);
        }
    }
}
