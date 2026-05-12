using BiSec.Library;
using Xunit;

namespace tests
{
    public class TransitionTests
    {
        [Fact]
        public void TestOpenDoor()
        {
            string data = "00000000010802020000000000000000";
            byte[] bytes = StringHelper.HexStringToByteArray(data);

            var transition = new Transition(bytes);

            Assert.True(transition.Hcp.PositionClose);
            Assert.False(transition.Hcp.PositionOpen);
        }

        [Fact]
        public void ParsesExstAfterGkWhenHcpIsNotPresent()
        {
            byte[] bytes = new byte[]
            {
                0x00, 0x00, 0x00, 0x00,
                0xFC, 0x01,
                0x01, 0x02, 0x03, 0x04,
                0x05, 0x06, 0x07, 0x08,
            };

            var transition = new Transition(bytes);

            Assert.Null(transition.Hcp);
            Assert.Equal(0xFC01, transition.Gk);
            Assert.Equal(new byte[] { 0x08, 0x07, 0x06, 0x05, 0x04, 0x03, 0x02, 0x01 }, transition.Exst);
        }

        [Fact]
        public void AppliesForecastLeadTimePostProcessing()
        {
            byte[] bytes = new byte[]
            {
                0x00, 0x00, 0x00, 0x00,
                0x01, 0x08,
                0x00, 0x01,
                0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00,
            };

            var transition = new Transition(bytes);

            Assert.Null(transition.Hcp);
            Assert.Equal(2, transition.DriveTime);
            Assert.True(transition.IgnoreRetries);
        }

        [Fact]
        public void EseActorDrivingOverridesDriveTimeToFive()
        {
            // gk = 0x0902 (ActorClasses.ESE), HCP byte 0 has driving bit (0x40) set.
            byte[] bytes = new byte[]
            {
                0x00, 0x00, 0x00, 0x00,
                0x09, 0x02,
                0x40, 0x00,
                0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00,
            };

            var transition = new Transition(bytes);

            Assert.NotNull(transition.Hcp);
            Assert.True(transition.Hcp.Driving);
            Assert.Equal(0x0902, transition.Gk);
            Assert.Equal(5, transition.DriveTime);
            Assert.True(transition.IgnoreRetries);
        }

        [Fact]
        public void NonEseDrivingActorStillReceivesDriveTimeTwo()
        {
            // gk = 0x0100 (not ESE), HCP driving bit set, driveTime starts at 0.
            byte[] bytes = new byte[]
            {
                0x00, 0x00, 0x00, 0x00,
                0x01, 0x00,
                0x40, 0x00,
                0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00,
            };

            var transition = new Transition(bytes);

            Assert.NotNull(transition.Hcp);
            Assert.True(transition.Hcp.Driving);
            Assert.Equal(2, transition.DriveTime);
            Assert.True(transition.IgnoreRetries);
        }
    }
}
