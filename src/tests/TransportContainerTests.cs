using BiSec.Library;
using System;
using System.Security.Cryptography;
using Xunit;

namespace tests
{
    public class TransportContainerTests
    {
        [Fact]
        public void GetNamePayloadSdk()
        {
            var message = new Message
            {
                Command = Command.GetName,
                Payload = PayloadFactory.Empty,
            };

            var packate = new Package("000000000000", "5410EC036150", message);
            byte[] bytes = packate.ToByteArray();
            string tcPayload = StringHelper.HexStringFromByteArray(bytes);

            string expectedPayload = "0000000000005410EC03615000090000000000262F4A";
            Assert.Equal(expectedPayload, tcPayload);
        }

        [Fact]
        public void GetNamePayloadSob()
        {
            var message = new Message
            {
                Command = Command.GetName,
                Payload = PayloadFactory.Empty,
            };

            Package package = new Package("000000000000", "5410EC852A31", message);
            byte[] bytes = package.ToByteArray();
            string tcPayload = StringHelper.HexStringFromByteArray(bytes);

            string expectedPayload = "0000000000005410EC852A3100090000000000262F5F";
            Assert.Equal(expectedPayload, tcPayload);
        }

        [Fact]
        public void PingResponse()
        {
            byte[] bytes = "5410EC03615000000000000600090200000000808B54".ToByteArray();
            var tc = Package.Load(bytes);

            Assert.Equal("5410EC036150", tc.Sender);
            Assert.Equal("000000000006", tc.Receiver);
            Assert.Equal(2, tc.Message.Tag);
            Assert.Equal("00000000", tc.Message.Token);
            Assert.Equal(Command.Ping, tc.Message.Command);
            Assert.Equal(tc.Message.CalculatedChecksum, tc.Message.Checksum);
            Assert.True(tc.IsCorrectChecksum);
        }

        [Fact]
        public void DecodePingHex()
        {
            string hex = "3534313045433835324133313030303030303030303030363030303930313030303030303030383038413637";
            byte[] bytes = StringHelper.HexStringToByteArray(hex);
            
            var container = Package.Load(bytes);

            Assert.Equal("000000000006", container.Receiver);
        }

        [Fact]
        public void DecodeBytes()
        {
            var bytes = new byte[]
            {
                0x30, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30, 0x35, 0x34, 0x31, 0x30, 0x45, 0x43, 0x38, 0x35, 0x32, 0x41, 0x33, 0x31,
                0x30, 0x30, 0x30, 0x39, 0x30, 0x31, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30, 0x41, 0x35, 0x31,
            };

            var bytes2 = new byte[]
            {
                0x35, 0x34, 0x31, 0x30, 0x45, 0x43, 0x38, 0x35, 0x32, 0x41, 0x33, 0x31, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30, 0x36,
                0x30, 0x30, 0x30, 0x39, 0x30, 0x31, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30, 0x38, 0x30, 0x38, 0x41, 0x36, 0x37,
            };

            var container = Package.Load(bytes2);

            Assert.Equal("5410EC852A31", container.Sender);
        }


        [Fact]
        public void GetStatesRequest()
        {
            // this one is with invalid checksum
            //var bytes = "0000000000065410EC852A31001D04A12E01BB067B22434D44223A224745545F56414C554553227D20EB".ToByteArray();

            var bytes = "0000000000065410EC852A31001D053FB13351067B22434D44223A224745545F56414C554553227D5517".ToByteArray();
            var tc = Package.Load(bytes);

            Assert.Equal(Command.Jmcp, tc.Message.Command);
        }
    }
}
