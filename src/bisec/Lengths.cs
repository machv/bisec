using System;
using System.Collections.Generic;
using System.Text;

namespace BiSec.Library
{
    public class Lengths
    {
        public const int BYTE_LENGTH = 2;
        public const int ADDRESS_BYTES = 6;
        public const int ADDRESS_SIZE = ADDRESS_BYTES * BYTE_LENGTH;
        public const int LENGTH_BYTES = 2;
        public const int LENGTH_SIZE = LENGTH_BYTES * BYTE_LENGTH;
        public const int TAG_BYTES = 1;
        public const int TAG_SIZE = TAG_BYTES * BYTE_LENGTH;
        public const int TOKEN_BYTES = 4;
        public const int TOKEN_SIZE = TOKEN_BYTES * BYTE_LENGTH;
        public const int COMMAND_BYTES = 1;
        public const int COMMAND_SIZE = COMMAND_BYTES * BYTE_LENGTH;
        public const int CHECKSUM_BYTES = 1;
        public const int CHECKSUM_SIZE = CHECKSUM_BYTES * BYTE_LENGTH;
        public const int MinimalMessageSize = LENGTH_SIZE + TAG_SIZE + TOKEN_SIZE + COMMAND_SIZE + CHECKSUM_SIZE;
        public const int MinimalMessageBytes = LENGTH_BYTES + TAG_BYTES + TOKEN_BYTES + COMMAND_BYTES + CHECKSUM_BYTES;
        public const int MinimalPackageBytes = ADDRESS_BYTES + ADDRESS_BYTES + MinimalMessageBytes + CHECKSUM_BYTES;
    }
}
