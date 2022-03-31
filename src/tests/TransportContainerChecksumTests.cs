using BiSec.Library;
using System;
using Xunit;

namespace tests
{
    public class TransportContainerChecksumTests
    {
        [Fact]
        public void Calculate()
        {
            var message = new Message
            {
                Command = Command.GetName,
                Payload = PayloadFactory.Empty,
            };

            var tp = new Package("000000000000", "5410EC036150", message);
            var cs = PackageChecksum.Calculate(tp);

            Assert.Equal(0x4A, cs);
        }

        [Fact]
        public void CalculateDirectly()
        {
            var message = new Message
            {
                Command = Command.GetName,
                Payload = PayloadFactory.Empty,
            };

            var cs = PackageChecksum.Calculate("000000000000", "5410EC036150", message);

            Assert.Equal(0x4A, cs);
        }
    }
}
