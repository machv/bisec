using System;
using System.Collections.Generic;

namespace BiSec.Library.Models
{
    public enum PortType
    {
        None = 0,
        Impuls = 1,
        AutoClose = 2,
        OnOff = 3,
        Up = 4,
        Down = 5,
        Half = 6,
        Walk = 7,
        Light = 8,
        On = 9,
        Off = 10,
        Lock = 11,
        Unlock = 12,
        OpenDoor = 13,
        Lift = 14,
        Sink = 15,
    }

    public static class PortTypes
    {
        // APK PortTypes.SORTING_RULE
        private static readonly PortType[] SortingRule =
        {
            PortType.Impuls,
            PortType.Light,
            PortType.Half,
            PortType.Walk,
            PortType.Up,
            PortType.Down,
            PortType.AutoClose,
            PortType.OnOff,
            PortType.On,
            PortType.Off,
            PortType.Lock,
            PortType.Unlock,
            PortType.OpenDoor,
            PortType.Lift,
            PortType.Sink,
        };

        public static IReadOnlyList<PortType> DisplayOrder { get; } = Array.AsReadOnly(SortingRule);

        // Types the app has no rule for sort after all others.
        public static int GetDisplayRank(this PortType portType)
        {
            int rank = Array.IndexOf(SortingRule, portType);

            return rank < 0 ? int.MaxValue : rank;
        }

        // APK PortTypes.portArraySorting
        public static int Compare(PortType x, PortType y)
        {
            return x.GetDisplayRank().CompareTo(y.GetDisplayRank());
        }
    }
}
