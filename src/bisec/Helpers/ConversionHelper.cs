using BiSec.Library.Exceptions;
using System;
using System.Collections.Generic;
using System.Text;

namespace BiSec.Library
{
    public static class ConversionHelper
    {
        public static byte[] IntToByteArray(int num, int size)
        {
            byte[] array = new byte[size];
            for (int i = 0; i < size; i++)
                array[i] = (byte)(num >> ((size - 1 - i) * 8));

            return array;
        }

        /// <summary>
        /// Converts hex string received as a byte array from a gateway to standard byte array.
        /// </summary>
        /// <param name="payload"></param>
        /// <returns></returns>
        public static byte[] GatewayPayloadToByteArray(byte[] payload)
        {
            if (payload.Length % 2 == 1)
                throw new InvalidPackageLengthException("The payload cannot has an odd length.");

            byte[] output = new byte[payload.Length >> 1];
            for (int i = 0; i < payload.Length >> 1; ++i)
                 //output[i] = (byte)((payload[i << 1] << 4) + payload[(i << 1) + 1]);
                output[i] = (byte)((StringHelper.GetHexValue(payload[i << 1]) << 4) + (StringHelper.GetHexValue(payload[(i << 1) + 1])));

            return output;
        }
    }
}
