using BiSec.Library.Models;
using System;
using Xunit;

namespace tests
{
    public class GroupCapabilityTests
    {
        [Fact]
        public void SwingGatesDifferInHalfAndWalkPorts()
        {
            Assert.Equal(
                new[] { PortType.Impuls, PortType.Half, PortType.Up, PortType.Down, PortType.Light },
                GroupType.SwingGateSingle.GetAllowedPortTypes());
            Assert.Equal(
                new[] { PortType.Impuls, PortType.Walk, PortType.Up, PortType.Down, PortType.Light },
                GroupType.SwingGateDouble.GetAllowedPortTypes());
        }

        [Fact]
        public void GroupTypeWithoutPortListAllowsNothing()
        {
            Assert.Empty(GroupType.None.GetAllowedPortTypes());
        }

        [Fact]
        public void SmartkeyGroupsCanAddButNotInheritPorts()
        {
            Assert.True(GroupType.Smartkey.HasFeature(GroupFeatures.AddPort));
            Assert.False(GroupType.Smartkey.HasFeature(GroupFeatures.InheritPort));
            Assert.True(GroupType.SectionalDoor.HasFeature(GroupFeatures.AddPort | GroupFeatures.InheritPort));
        }

        [Fact]
        public void PortsSortInTheAppsDisplayOrder()
        {
            var ports = new[] { PortType.None, PortType.Down, PortType.Up, PortType.Half, PortType.Light, PortType.Impuls };

            Array.Sort(ports, PortTypes.Compare);

            Assert.Equal(
                new[] { PortType.Impuls, PortType.Light, PortType.Half, PortType.Up, PortType.Down, PortType.None },
                ports);
        }
    }
}
