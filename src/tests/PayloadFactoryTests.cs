using BiSec.Library;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace tests
{
    public class PayloadFactoryTests
    {
        // Looked up by name so every row shows up as its own test case.
        private static readonly Dictionary<string, Func<Payload>> Builders = new Dictionary<string, Func<Payload>>
        {
            ["Empty"] = () => PayloadFactory.Empty,
            ["Login, non-ASCII name"] = () => PayloadFactory.Login("j\u00F6rg", "pw"),
            ["AddUser"] = () => PayloadFactory.AddUser("ben", "secret"),
            ["AddUser, non-ASCII name"] = () => PayloadFactory.AddUser("\u00C4nne", "pw"),
            ["RemoveUser"] = () => PayloadFactory.RemoveUser(3),
            ["ChangePassword"] = () => PayloadFactory.ChangePassword("new"),
            ["ChangePasswordOfUser"] = () => PayloadFactory.ChangePasswordOfUser(3, "new"),
            ["ChangeUserName"] = () => PayloadFactory.ChangeUserName("eva"),
            ["ChangeUserNameOfUser"] = () => PayloadFactory.ChangeUserNameOfUser(4, "eva"),
            ["GetUserRights"] = () => PayloadFactory.GetUserRights(2),
            ["SetUserRights"] = () => PayloadFactory.SetUserRights(2, new[] { 0, 5, 15 }),
            ["SetUserRights, no groups"] = () => PayloadFactory.SetUserRights(2, new int[0]),
            ["SetName"] = () => PayloadFactory.SetName("GW \u00DC"),
            ["SetValue"] = () => PayloadFactory.SetValue(17, 255),
            ["GetValue"] = () => PayloadFactory.GetValue(63),
            ["RemoveGroup"] = () => PayloadFactory.RemoveGroup(1),
            ["SetGroupName"] = () => PayloadFactory.SetGroupName(1, "Tor"),
            ["SetGroupedPorts"] = () => PayloadFactory.SetGroupedPorts(1, new[] { 0, 4, 15 }),
            ["RemovePort"] = () => PayloadFactory.RemovePort(15),
            ["GetPortType"] = () => PayloadFactory.GetPortType(3),
            ["SetPortType"] = () => PayloadFactory.SetPortType(3, 8),
            ["GetTransition"] = () => PayloadFactory.GetTransition(2),
            ["SetState, default state"] = () => PayloadFactory.SetState(2),
            ["SetState"] = () => PayloadFactory.SetState(2, 1),
        };

        private static readonly Dictionary<string, Action> Rejected = new Dictionary<string, Action>
        {
            ["RemoveUser 256"] = () => PayloadFactory.RemoveUser(256),
            ["RemoveUser -1"] = () => PayloadFactory.RemoveUser(-1),
            ["ChangePasswordOfUser 256"] = () => PayloadFactory.ChangePasswordOfUser(256, "new"),
            ["ChangeUserNameOfUser 256"] = () => PayloadFactory.ChangeUserNameOfUser(256, "eva"),
            ["GetUserRights 256"] = () => PayloadFactory.GetUserRights(256),
            ["SetUserRights user 256"] = () => PayloadFactory.SetUserRights(256, new[] { 0 }),
            ["SetUserRights group 256"] = () => PayloadFactory.SetUserRights(1, new[] { 0, 256 }),
            ["SetValue key 256"] = () => PayloadFactory.SetValue(256, 0),
            ["SetValue value 256"] = () => PayloadFactory.SetValue(0, 256),
            ["SetValue value -1"] = () => PayloadFactory.SetValue(0, -1),
            ["GetValue 256"] = () => PayloadFactory.GetValue(256),
            ["RemoveGroup 256"] = () => PayloadFactory.RemoveGroup(256),
            ["SetGroupName 256"] = () => PayloadFactory.SetGroupName(256, "Tor"),
            ["SetGroupedPorts group 256"] = () => PayloadFactory.SetGroupedPorts(256, new[] { 0 }),
            ["SetGroupedPorts port -1"] = () => PayloadFactory.SetGroupedPorts(1, new[] { -1 }),
            ["RemovePort 256"] = () => PayloadFactory.RemovePort(256),
            ["GetPortType 256"] = () => PayloadFactory.GetPortType(256),
            ["SetPortType port 256"] = () => PayloadFactory.SetPortType(256, 1),
            ["SetPortType type 256"] = () => PayloadFactory.SetPortType(1, 256),
            ["GetTransition 256"] = () => PayloadFactory.GetTransition(256),
            ["SetState port 256"] = () => PayloadFactory.SetState(256),
            ["SetState state 256"] = () => PayloadFactory.SetState(1, 256),
        };

        public static IEnumerable<object[]> RejectedNames => Rejected.Keys.Select(name => new object[] { name });

        [Theory]
        [InlineData("Empty", "")]
        // [5]["j", 0xC3 0xB6, "r", "g"]["pw"]: the length counts bytes, not characters.
        [InlineData("Login, non-ASCII name", "056AC3B672677077")]
        [InlineData("AddUser", "0362656E736563726574")]
        [InlineData("AddUser, non-ASCII name", "05C3846E6E657077")]
        [InlineData("RemoveUser", "03")]
        [InlineData("ChangePassword", "6E6577")]
        [InlineData("ChangePasswordOfUser", "036E6577")]
        [InlineData("ChangeUserName", "657661")]
        [InlineData("ChangeUserNameOfUser", "04657661")]
        [InlineData("GetUserRights", "02")]
        [InlineData("SetUserRights", "0200050F")]
        [InlineData("SetUserRights, no groups", "02")]
        [InlineData("SetName", "475720C39C")]
        [InlineData("SetValue", "11FF")]
        [InlineData("GetValue", "3F")]
        [InlineData("RemoveGroup", "01")]
        [InlineData("SetGroupName", "01546F72")]
        [InlineData("SetGroupedPorts", "0100040F")]
        [InlineData("RemovePort", "0F")]
        [InlineData("GetPortType", "03")]
        [InlineData("SetPortType", "0308")]
        [InlineData("GetTransition", "02")]
        [InlineData("SetState, default state", "02FF")]
        [InlineData("SetState", "0201")]
        public void BuildsTheExactBytes(string builder, string expectedHex)
        {
            Payload payload = Builders[builder]();

            Assert.Equal(expectedHex, StringHelper.HexStringFromByteArray(payload.ToByteArray()));
        }

        [Theory]
        [InlineData("GET_VALUES", "{\"cmd\":\"GET_VALUES\"}")]
        [InlineData("GET_GROUPS", "{\"cmd\":\"GET_GROUPS\"}")]
        [InlineData("GET_USERS", "{\"cmd\":\"GET_USERS\"}")]
        public void BuildsTheJmcpRequests(string request, string expectedJson)
        {
            Payload payload = request switch
            {
                "GET_VALUES" => PayloadFactory.GetValues(),
                "GET_GROUPS" => PayloadFactory.GetGroups(),
                _ => PayloadFactory.GetUsers(),
            };

            Assert.Equal(expectedJson, payload.TextContent);
        }

        [Theory]
        [MemberData(nameof(RejectedNames))]
        public void RejectsValuesThatDoNotFitAByte(string call)
        {
            Assert.Throws<ArgumentOutOfRangeException>(Rejected[call]);
        }

        [Fact]
        public void RejectsAUserNameLongerThan255Bytes()
        {
            // 128 characters, 256 bytes.
            string name = new string('\u00E4', 128);

            Assert.Throws<ArgumentException>(() => PayloadFactory.Login(name, "pw"));
            Assert.Throws<ArgumentException>(() => PayloadFactory.AddUser(name, "pw"));

            // 127 characters, 254 bytes.
            Assert.Equal(254, PayloadFactory.Login(name.Substring(1), "").ToByteArray()[0]);
        }

        [Fact]
        public void RejectsMissingText()
        {
            Assert.Throws<ArgumentNullException>(() => PayloadFactory.Login(null, "pw"));
            Assert.Throws<ArgumentNullException>(() => PayloadFactory.Login("name", null));
            Assert.Throws<ArgumentNullException>(() => PayloadFactory.SetName(null));
            Assert.Throws<ArgumentNullException>(() => PayloadFactory.SetGroupName(1, null));
            Assert.Throws<ArgumentNullException>(() => PayloadFactory.SetUserRights(1, null));
            Assert.Throws<ArgumentNullException>(() => PayloadFactory.Jmcp(null));
        }
    }
}
