using System;

namespace BiSec.Library.Exceptions
{
    /// <summary>
    /// The gateway answered a request with an ERROR package.
    /// </summary>
    public class GatewayException : Exception
    {
        public Command Command { get; }
        public Error Error { get; }

        public GatewayException(Command command, Error error)
            : base($"Gateway rejected {command?.Name ?? "request"} with error {error} (0x{(int)error:X2}).")
        {
            Command = command;
            Error = error;
        }
    }
}
