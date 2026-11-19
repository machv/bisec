namespace BiSec.Library.Models
{
    public enum WifiState : byte
    {
        Connected = 0,
        NotConnected = 1,
        Busy = 64,
        ApNotFound = 128,
        SecurityMismatch = 129,
        AuthenticationFailure = 130,
        ConnectionFailure = 131,
    }

    public static class WifiStateExtensions
    {
        /// <summary>Bit 7 marks a failed connection attempt.</summary>
        public static bool IsFailure(this WifiState state)
        {
            return ((byte)state & 128) == 128;
        }

        /// <summary>Bit 6 marks a connection attempt in progress.</summary>
        public static bool IsBusy(this WifiState state)
        {
            return !state.IsFailure() && ((byte)state & 64) == 64;
        }
    }
}
