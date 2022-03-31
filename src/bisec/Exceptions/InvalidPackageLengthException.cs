using System;
using System.Collections.Generic;
using System.Text;

namespace BiSec.Library.Exceptions
{
    class InvalidPackageLengthException : Exception
    {
        public InvalidPackageLengthException() { }
        public InvalidPackageLengthException(string message) : base(message) { }
    }
}
