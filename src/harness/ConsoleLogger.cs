using Microsoft.Extensions.Logging;
using System;

namespace BiSec.Harness
{
    /// <summary>
    /// Writes library log output to stderr. With --trace this includes every package on the wire.
    /// </summary>
    internal sealed class ConsoleLogger : ILogger
    {
        private readonly LogLevel _minimumLevel;

        public ConsoleLogger(LogLevel minimumLevel)
        {
            _minimumLevel = minimumLevel;
        }

        public IDisposable BeginScope<TState>(TState state)
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return logLevel >= _minimumLevel;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;

            Console.Error.WriteLine($"[{logLevel}] {formatter(state, exception)}");
        }
    }
}
