using BiSec.Library;
using BiSec.Library.Models;
using System;
using System.Collections.Generic;

namespace BiSec.Harness
{
    /// <summary>
    /// Prints a transition: the raw fields first, then what the library makes of them.
    /// </summary>
    internal static class StateOutput
    {
        public static void Print(Transition transition, Group group)
        {
            if (transition == null)
            {
                Console.WriteLine("  no state");
                return;
            }

            Console.WriteLine($"  [{transition.Time:HH:mm:ss}] actual {transition.ActualState} ({transition.ActualState / 2.0:0.#} %), desired {transition.RequestedState}, drive time {transition.DriveTime} s, error {transition.Error}, auto close {transition.AutoClose}");
            Console.WriteLine($"  gk 0x{transition.Gk:X4} ({transition.ActorClass}), exst [{StringHelper.HexStringFromByteArray(transition.Exst, " ").Trim()}]");
            Console.WriteLine($"  hcp: {DescribeHcp(transition.Hcp)}");

            ActorStateInfo info = ActorStateInterpreter.Interpret(transition, group?.Type ?? GroupType.None);

            List<string> parts = new List<string>
            {
                $"helper {info.Helper}",
                $"state {info.State}",
            };

            if (info.SecondLeafState != null)
                parts.Add($"second leaf {info.SecondLeafState}");

            if (info.PositionPercent != null)
                parts.Add($"{info.PositionPercent:0.#} %{(info.IsDefinedHalfOpen ? " (H)" : "")}");

            if (info.IsDriving)
                parts.Add($"driving {info.DrivingDirection?.ToString() ?? "(direction unknown)"}");

            if (info.HasError)
                parts.Add("ERROR");

            if (info.NotReferenced)
                parts.Add("not referenced");

            if (info.AutoClose)
                parts.Add("auto close pending");

            if (info.Lock != null)
                parts.Add($"lock {info.Lock}");

            if (info.PermanentUnlock != null)
                parts.Add($"permanent unlock {info.PermanentUnlock}");

            if (info.StopActive != null)
                parts.Add($"stop active {info.StopActive}");

            if (info.Battery != null)
                parts.Add($"battery {info.Battery}");

            if (info.Bollard != null)
                parts.Add($"bollard {info.Bollard}");

            if (info.IsOn != null)
                parts.Add(info.IsOn.Value ? "on" : "off");

            if (info.Message != StatusMessage.None)
                parts.Add($"message {info.Message}");

            if (info.ValueMessage != StatusMessage.None)
                parts.Add($"value {info.ValueMessage}");

            Console.WriteLine($"  => {string.Join(", ", parts)}");

            if (group == null)
                Console.WriteLine("     (interpreted without a group type)");
        }

        private static string DescribeHcp(Hcp hcp)
        {
            if (hcp == null)
                return "none";

            List<string> flags = new List<string>();
            Add(flags, hcp.PositionOpen, "position open");
            Add(flags, hcp.PositionClose, "position closed");
            Add(flags, hcp.OptionRelais, "option relay");
            Add(flags, hcp.LightBarrier, "light barrier");
            Add(flags, hcp.Error, "error");
            Add(flags, hcp.DrivingToClose, "driving to close");
            Add(flags, hcp.Driving, "driving");
            Add(flags, hcp.HalfOpen, "half opened");
            Add(flags, hcp.ForecastLeadTime, "forecast lead time");
            Add(flags, hcp.Learned, "learned");
            Add(flags, hcp.NotReferenced, "not referenced");

            return flags.Count == 0 ? "no flags set" : string.Join(", ", flags);
        }

        private static void Add(List<string> flags, bool value, string name)
        {
            if (value)
                flags.Add(name);
        }
    }
}
