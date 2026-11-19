using System;
using System.Collections.Generic;

namespace BiSec.Harness
{
    /// <summary>
    /// Command line of the harness: one command, its positional arguments and --flags.
    /// Connection settings fall back to BISECUR_* environment variables.
    /// </summary>
    internal sealed class Options
    {
        private static readonly HashSet<string> Switches = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "write", "yes", "trace", "learn",
        };

        private readonly Dictionary<string, string> _values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _switches = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public string Command { get; private set; }
        public List<string> Arguments { get; } = new List<string>();

        public string Host => Get("host", "BISECUR_HOST");
        public string Mac => Get("mac", "BISECUR_MAC");
        public string User => Get("user", "BISECUR_USER");
        public string Password => Get("password", "BISECUR_PASSWORD");
        public bool Write => _switches.Contains("write");
        public bool Yes => _switches.Contains("yes");
        public bool Trace => _switches.Contains("trace");
        public bool Learn => _switches.Contains("learn");

        public int Port => GetInt("port", BiSec.Library.GatewayConnection.DefaultPort);
        public int TimeoutSeconds => GetInt("timeout", 3);

        public static Options Parse(string[] args)
        {
            Options options = new Options();

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                if (arg.StartsWith("--", StringComparison.Ordinal))
                {
                    string name = arg.Substring(2);
                    if (Switches.Contains(name))
                    {
                        options._switches.Add(name);
                        continue;
                    }

                    if (i + 1 >= args.Length)
                        throw new ArgumentException($"Option --{name} needs a value.");

                    options._values[name] = args[++i];
                }
                else if (options.Command == null)
                {
                    options.Command = arg.ToLowerInvariant();
                }
                else
                {
                    options.Arguments.Add(arg);
                }
            }

            return options;
        }

        public string Argument(int index, string name)
        {
            if (index >= Arguments.Count)
                throw new ArgumentException($"Missing argument <{name}>.");

            return Arguments[index];
        }

        public int IntArgument(int index, string name)
        {
            string text = Argument(index, name);
            if (!int.TryParse(text, out int value))
                throw new ArgumentException($"Argument <{name}> must be a number, got '{text}'.");

            return value;
        }

        private string Get(string name, string environmentVariable)
        {
            if (_values.TryGetValue(name, out string value))
                return value;

            value = Environment.GetEnvironmentVariable(environmentVariable);

            return string.IsNullOrEmpty(value) ? null : value;
        }

        private int GetInt(string name, int fallback)
        {
            if (!_values.TryGetValue(name, out string text))
                return fallback;

            if (!int.TryParse(text, out int value))
                throw new ArgumentException($"Option --{name} must be a number, got '{text}'.");

            return value;
        }
    }
}
