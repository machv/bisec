namespace BiSec.Library.Models
{
    /// <summary>
    /// Fixed limits and well-known ids of a gateway, as used by the legacy app.
    /// </summary>
    public static class GatewayLimits
    {
        public const int MaxPorts = 16;
        public const int MaxValues = 64;
        public const int MaxUsers = 10;
        public const int AdminUserId = 0;

        /// <summary>
        /// Stored as a group's requestable port when the group cannot report its state.
        /// </summary>
        public const int NoPort = 255;
    }
}
