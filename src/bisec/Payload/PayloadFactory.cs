using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace BiSec.Library
{
    public static class PayloadFactory
    {
        public static Payload Empty => new Payload(new byte[0]);

        public static Payload Login(string userName, string password)
        {
            using (var ms = new MemoryStream())
            {
                ms.WriteByte((byte)userName.Length);

                var usernameBytes = userName.ToByteArray();
                ms.Write(usernameBytes, 0, usernameBytes.Length);

                var passwordBytes = password.ToByteArray();
                ms.Write(passwordBytes, 0, passwordBytes.Length);

                var content = ms.ToArray();

                Payload payload = new Payload(content);

                return payload;
            }
        }

        public static Payload Jmcp(string content)
        {
            byte[] data = content.ToByteArray();
            return new Payload(data, PayloadType.Jmcp);
        }

        public static Payload GetValues()
        {
            return Jmcp("{\"cmd\":\"GET_VALUES\"}");
        }

        public static Payload GetGroups()
        {
            return Jmcp("{\"cmd\":\"GET_GROUPS\"}");
        }

        public static Payload GetGroupsForUser()
        {
            return Jmcp("{\"cmd\":\"GET_GROUPS\", \"FORUSER\":1}");
        }

        public static Payload GetUsers()
        {
            return Jmcp("{\"cmd\":\"GET_USERS\"}");
        }

        public static Payload AddUser(string userName, string password)
        {
            using (var ms = new MemoryStream())
            {
                ms.WriteByte((byte)userName.Length);

                var usernameBytes = userName.ToByteArray();
                ms.Write(usernameBytes, 0, usernameBytes.Length);

                var passwordBytes = password.ToByteArray();
                ms.Write(passwordBytes, 0, passwordBytes.Length);

                var content = ms.ToArray();

                Payload payload = new Payload(content);

                return payload;
            }
        }

        public static Payload SetUserRights(int id, int[] groups)
        {
            using (var ms = new MemoryStream())
            {
                ms.WriteByte((byte)id);
                foreach (int groupId in groups)
                    ms.WriteByte((byte)groupId);

                var content = ms.ToArray();
                Payload payload = new Payload(content);

                return payload;
            }
        }

        public static Payload GetTransition(int portId)
        {
            byte[] data = new byte[] {
                (byte)portId,
            };

            return new Payload(data);
        }

        public static Payload SetState(int portId, int state = 0xff)
        {
            byte[] data = new byte[] {
                (byte)portId,
                (byte)state,
            };

            return new Payload(data);
        }
    }
}
