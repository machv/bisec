using System;
using System.Collections.Concurrent;

namespace BiSec.Library
{
    /// <summary>
    /// How long to wait for the response to a request, per command, in milliseconds.
    /// Defaults are the legacy app's Timings.MCPPROCESSOR_REQUEST_TIMEOUTS.
    /// </summary>
    public class RequestTimeouts
    {
        private readonly ConcurrentDictionary<int, int> _timeouts = new ConcurrentDictionary<int, int>();

        public int Default { get; set; } = 15000;

        public RequestTimeouts()
        {
            Set(Command.GetName, 4000);
            Set(Command.Login, 10000);
            Set(Command.ChangePassword, 10000);
            Set(Command.ScanWifi, 10000);
            Set(Command.AddPort, 25000);
            Set(Command.InheritPort, 25000);
        }

        public int Get(Command command)
        {
            if (command != null && _timeouts.TryGetValue(command.Code, out int timeout))
                return timeout;

            return Default;
        }

        public void Set(Command command, int milliseconds)
        {
            if (command == null)
                throw new ArgumentNullException(nameof(command));

            if (milliseconds <= 0)
                throw new ArgumentOutOfRangeException(nameof(milliseconds));

            _timeouts[command.Code] = milliseconds;
        }
    }
}
