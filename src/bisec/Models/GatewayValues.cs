using System;

namespace BiSec.Library.Models
{
    /// <summary>
    /// Layout of the gateway's 64-entry key-value store (JMCP GET_VALUES / SET_VALUE),
    /// as the legacy app's GatewayValues uses it.
    /// </summary>
    public static class GatewayValues
    {
        /// <summary>Key holding the <see cref="GroupType"/> of a group.</summary>
        public static int GroupTypeKey(int groupId)
        {
            CheckGroupId(groupId);
            return groupId;
        }

        /// <summary>Key holding the id of the port used to request a group's state.</summary>
        public static int RequestablePortKey(int groupId)
        {
            CheckGroupId(groupId);
            return groupId + GatewayLimits.MaxPorts;
        }

        private static void CheckGroupId(int groupId)
        {
            if (groupId < 0 || groupId >= GatewayLimits.MaxPorts)
                throw new ArgumentOutOfRangeException(nameof(groupId), groupId, $"A group id must be between 0 and {GatewayLimits.MaxPorts - 1}.");
        }
    }
}
