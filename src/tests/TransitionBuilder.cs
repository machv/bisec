using BiSec.Library;
using System.Collections.Generic;

namespace tests
{
    internal static class TransitionBuilder
    {
        // exst4..exst7 are indexes into Transition.Exst (the app's exst array);
        // on the wire the eight exst bytes are sent in reverse order.
        public static Transition Build(
            int gk,
            int actual = 0,
            int desired = 0,
            int flags = 0,
            int driveLow = 0,
            int hcp1 = 0,
            int hcp2 = 0,
            int exst4 = 0,
            int exst5 = 0,
            int exst6 = 0,
            int exst7 = 0)
        {
            var bytes = new List<byte>
            {
                (byte)actual, (byte)desired, (byte)flags, (byte)driveLow,
                (byte)(gk >> 8), (byte)gk,
            };

            if ((gk >> 8) < 252)
            {
                bytes.Add((byte)hcp1);
                bytes.Add((byte)hcp2);
            }

            bytes.AddRange(new byte[] { (byte)exst7, (byte)exst6, (byte)exst5, (byte)exst4, 0, 0, 0, 0 });

            return new Transition(bytes.ToArray());
        }
    }
}
