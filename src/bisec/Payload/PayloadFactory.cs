using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace BiSec.Library
{
    /// <summary>
    /// Builds request payloads. The byte layouts mirror the legacy app's MCPBuilder.
    /// </summary>
    public static class PayloadFactory
    {
        public static Payload Empty => new Payload(new byte[0]);

        public static Payload Login(string userName, string password)
        {
            return LengthPrefixedNameAndText(userName, password);
        }

        public static Payload AddUser(string userName, string password)
        {
            return LengthPrefixedNameAndText(userName, password);
        }

        public static Payload RemoveUser(int userId)
        {
            return Bytes(ToByte(userId, nameof(userId)));
        }

        public static Payload ChangePassword(string password)
        {
            return Text(password);
        }

        public static Payload ChangePasswordOfUser(int userId, string password)
        {
            return ByteAndText(ToByte(userId, nameof(userId)), password);
        }

        public static Payload ChangeUserName(string userName)
        {
            return Text(userName);
        }

        public static Payload ChangeUserNameOfUser(int userId, string userName)
        {
            return ByteAndText(ToByte(userId, nameof(userId)), userName);
        }

        public static Payload GetUserRights(int userId)
        {
            return Bytes(ToByte(userId, nameof(userId)));
        }

        public static Payload SetUserRights(int userId, IEnumerable<int> groupIds)
        {
            return ByteAndList(ToByte(userId, nameof(userId)), groupIds, nameof(groupIds));
        }

        public static Payload SetName(string gatewayName)
        {
            return Text(gatewayName);
        }

        public static Payload SetValue(int key, int value)
        {
            return Bytes(ToByte(key, nameof(key)), ToByte(value, nameof(value)));
        }

        public static Payload GetValue(int key)
        {
            return Bytes(ToByte(key, nameof(key)));
        }

        public static Payload RemoveGroup(int groupId)
        {
            return Bytes(ToByte(groupId, nameof(groupId)));
        }

        public static Payload SetGroupName(int groupId, string groupName)
        {
            return ByteAndText(ToByte(groupId, nameof(groupId)), groupName);
        }

        public static Payload SetGroupedPorts(int groupId, IEnumerable<int> portIds)
        {
            return ByteAndList(ToByte(groupId, nameof(groupId)), portIds, nameof(portIds));
        }

        public static Payload RemovePort(int portId)
        {
            return Bytes(ToByte(portId, nameof(portId)));
        }

        public static Payload GetPortType(int portId)
        {
            return Bytes(ToByte(portId, nameof(portId)));
        }

        public static Payload SetPortType(int portId, int portType)
        {
            return Bytes(ToByte(portId, nameof(portId)), ToByte(portType, nameof(portType)));
        }

        public static Payload GetTransition(int portId)
        {
            return Bytes(ToByte(portId, nameof(portId)));
        }

        public static Payload SetState(int portId, int state = 0xff)
        {
            return Bytes(ToByte(portId, nameof(portId)), ToByte(state, nameof(state)));
        }

        public static Payload Jmcp(string content)
        {
            if (content == null)
                throw new ArgumentNullException(nameof(content));

            return new Payload(content.ToByteArray(), PayloadType.Jmcp);
        }

        public static Payload GetValues()
        {
            return Jmcp("{\"cmd\":\"GET_VALUES\"}");
        }

        public static Payload GetGroups()
        {
            return Jmcp("{\"cmd\":\"GET_GROUPS\"}");
        }

        public static Payload GetGroupsForUser(int userId)
        {
            return Jmcp($"{{\"cmd\":\"GET_GROUPS\", \"forUser\":{userId}}}");
        }

        public static Payload GetUsers()
        {
            return Jmcp("{\"cmd\":\"GET_USERS\"}");
        }

        private static Payload Bytes(params byte[] data)
        {
            return new Payload(data);
        }

        private static Payload Text(string text)
        {
            if (text == null)
                throw new ArgumentNullException(nameof(text));

            return new Payload(text.ToByteArray());
        }

        private static Payload ByteAndText(byte first, string text)
        {
            if (text == null)
                throw new ArgumentNullException(nameof(text));

            using (var ms = new MemoryStream())
            {
                ms.WriteByte(first);

                var textBytes = text.ToByteArray();
                ms.Write(textBytes, 0, textBytes.Length);

                return new Payload(ms.ToArray());
            }
        }

        private static Payload ByteAndList(byte first, IEnumerable<int> values, string parameterName)
        {
            if (values == null)
                throw new ArgumentNullException(parameterName);

            using (var ms = new MemoryStream())
            {
                ms.WriteByte(first);
                foreach (int value in values)
                    ms.WriteByte(ToByte(value, parameterName));

                return new Payload(ms.ToArray());
            }
        }

        // [nameByteLength][name][text]; the app writes the UTF-8 byte length, not the character count.
        private static Payload LengthPrefixedNameAndText(string name, string text)
        {
            if (name == null)
                throw new ArgumentNullException(nameof(name));

            if (text == null)
                throw new ArgumentNullException(nameof(text));

            var nameBytes = name.ToByteArray();
            if (nameBytes.Length > byte.MaxValue)
                throw new ArgumentException("The name is longer than 255 bytes.", nameof(name));

            using (var ms = new MemoryStream())
            {
                ms.WriteByte((byte)nameBytes.Length);
                ms.Write(nameBytes, 0, nameBytes.Length);

                var textBytes = text.ToByteArray();
                ms.Write(textBytes, 0, textBytes.Length);

                return new Payload(ms.ToArray());
            }
        }

        private static byte ToByte(int value, string parameterName)
        {
            if (value < byte.MinValue || value > byte.MaxValue)
                throw new ArgumentOutOfRangeException(parameterName, value, "The value must fit into one byte (0..255).");

            return (byte)value;
        }
    }
}
