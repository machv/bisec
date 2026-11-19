using System;

namespace BiSec.Library.Exceptions
{
    /// <summary>
    /// The gateway answered with something the protocol does not allow at that point
    /// (wrong command, missing or truncated payload).
    /// </summary>
    public class GatewayProtocolException : Exception
    {
        public GatewayProtocolException(string message) : base(message) { }

        public GatewayProtocolException(string message, Exception innerException) : base(message, innerException) { }
    }
}
