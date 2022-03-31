using BiSec.Library;
using Xunit;

namespace tests
{
    public class BiSecurMessageTests
    {
        [Fact]
        public void GetName()
        {
            var testPackage = new Message() { 
                Command = Command.GetName,
                Payload = PayloadFactory.Empty,
            };

            byte[] bytes = testPackage.ToBytes();
            string message = StringHelper.HexStringFromByteArray(bytes);

            Assert.Equal("00090000000000262F", message);
        }

        [Fact]
        public void Login()
        {
            var m = Message.Login("thomas", "aaabbbccc");
            byte[] bytes = m.ToBytes();
            string message = StringHelper.HexStringFromByteArray(bytes);

            Assert.Equal("00190000000000100674686F6D61736161616262626363632D", message);
        }

        [Fact]
        public void Jcmp()
        {
            var message = Message.Jmcp("{\"cmd\":\"GET_VALUES\"}");
            var byteArray = message.ToBytes();
            var m = StringHelper.HexStringFromByteArray(byteArray);

            Assert.Equal("001D0000000000067B22636D64223A224745545F56414C554553227D20", m);
        }
    }
}
