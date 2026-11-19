using System;
using System.Collections.Generic;

namespace BiSec.Library.Models
{
    public enum GroupType
    {
        None = 0,
        SectionalDoor = 1,
        HorizontalSectionalDoor = 2,
        SwingGateSingle = 3,
        SwingGateDouble = 4,
        SlidingGate = 5,
        Door = 6,
        Light = 7,
        Other = 8,
        Jack = 9,
        Smartkey = 10,
        PilomatPoller = 11,
        PilomatDurchfahrtssperre = 12,
        PilomatHubbalken = 13,
        PilomatReifenkiller = 14,
        Barrier = 15,
    }

    public static class GroupTypes
    {
        private static readonly IReadOnlyList<PortType> NoPorts = Array.AsReadOnly(new PortType[0]);

        // APK GroupTypes.PORTS
        private static readonly Dictionary<GroupType, IReadOnlyList<PortType>> Ports = new Dictionary<GroupType, IReadOnlyList<PortType>>
        {
            [GroupType.SectionalDoor] = Create(PortType.Impuls, PortType.Up, PortType.Down, PortType.Half, PortType.Light),
            [GroupType.HorizontalSectionalDoor] = Create(PortType.Impuls, PortType.Up, PortType.Down, PortType.Half, PortType.Light),
            [GroupType.SwingGateSingle] = Create(PortType.Impuls, PortType.Half, PortType.Up, PortType.Down, PortType.Light),
            [GroupType.SwingGateDouble] = Create(PortType.Impuls, PortType.Walk, PortType.Up, PortType.Down, PortType.Light),
            [GroupType.SlidingGate] = Create(PortType.Impuls, PortType.Up, PortType.Down, PortType.Half, PortType.Light),
            [GroupType.Door] = Create(PortType.AutoClose, PortType.Impuls, PortType.Light, PortType.OnOff),
            [GroupType.Light] = Create(PortType.On, PortType.Off, PortType.OnOff, PortType.Impuls),
            [GroupType.Other] = Create(PortType.On, PortType.Off, PortType.OnOff, PortType.Impuls),
            [GroupType.Jack] = Create(PortType.On, PortType.Off, PortType.OnOff, PortType.Impuls),
            [GroupType.Smartkey] = Create(PortType.Lock, PortType.Unlock, PortType.OpenDoor),
            [GroupType.PilomatPoller] = Create(PortType.Lift, PortType.Sink),
            [GroupType.PilomatDurchfahrtssperre] = Create(PortType.Lift, PortType.Sink),
            [GroupType.PilomatHubbalken] = Create(PortType.Lift, PortType.Sink),
            [GroupType.PilomatReifenkiller] = Create(PortType.Lift, PortType.Sink),
            [GroupType.Barrier] = Create(PortType.Impuls, PortType.Up, PortType.Down),
        };

        public static IReadOnlyList<PortType> GetAllowedPortTypes(this GroupType groupType)
        {
            return Ports.TryGetValue(groupType, out IReadOnlyList<PortType> ports) ? ports : NoPorts;
        }

        // APK HmGroup.features
        public static GroupFeatures GetFeatures(this GroupType groupType)
        {
            if (groupType == GroupType.Smartkey)
                return GroupFeatures.AddPort;

            return GroupFeatures.AddPort | GroupFeatures.InheritPort;
        }

        public static bool HasFeature(this GroupType groupType, GroupFeatures feature)
        {
            return (groupType.GetFeatures() & feature) == feature;
        }

        private static IReadOnlyList<PortType> Create(params PortType[] portTypes)
        {
            return Array.AsReadOnly(portTypes);
        }
    }
}
