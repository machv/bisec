using BiSec.Library;
using Xunit;

namespace tests
{
    public class BiSecurMessageChecksumTests
    {
        [Fact]
        public void HexStringToInt()
        {
            int hexInt = StringHelper.ToHexInt("5D89F13A");

            Assert.Equal(0x5D89F13A, hexInt);

        }
        [Fact]
        public void CalculateOne()
        {
            var message = new Message()
            {
                Command = Command.Login,
                Payload = PayloadFactory.Login("thomas", "aaabbbccc"),
            };

            var checksum = new MessageChecksum(message).Calculate();

            Assert.Equal(0x2d, checksum);
        }

        [Fact]
        public void CalculateTwo()
        {
            var message = new Message()
            {
                Command = Command.Jmcp,
                Payload = PayloadFactory.GetUsers(),
                Token = "5D89F13A",
                Tag = 1,
            };

            var checksum = new MessageChecksum(message).Calculate();

            Assert.Equal(0xf3, checksum);
        }

        [Fact]
        public void ToHexString()
        {
            int number = 45;

            byte[] bytes = new byte[] {
                (byte)number
            };

            string hex = StringHelper.HexStringFromByteArray(bytes);

            Assert.Equal("2D", hex);
        }
    }
}
