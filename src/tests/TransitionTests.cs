using BiSec.Library;
using System;
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

        [Theory]
        [InlineData(0x60, 10, 200, DriveDirection.TO_CLOSE)]
        [InlineData(0x40, 200, 0, DriveDirection.TO_OPEN)]
        public void DrivingDirectionComesFromHcpWhenPresent(int hcp1, int actual, int desired, DriveDirection expected)
        {
            // actual/desired point the other way in both rows; the HCP flag wins.
            var transition = TransitionBuilder.Build(0x0100, actual, desired, hcp1: hcp1);

            Assert.True(transition.IsDriving);
            Assert.Equal(expected, transition.DrivingDirection);
        }

        [Theory]
        [InlineData(200, 0, DriveDirection.TO_CLOSE)]
        [InlineData(0, 200, DriveDirection.TO_OPEN)]
        [InlineData(100, 100, null)]
        public void DrivingDirectionFallsBackToActualAndDesiredWithoutHcp(int actual, int desired, DriveDirection? expected)
        {
            var transition = TransitionBuilder.Build(0xFC00, actual, desired, driveLow: 5);

            Assert.Null(transition.Hcp);
            Assert.True(transition.IsDriving);
            Assert.Equal(expected, transition.DrivingDirection);
        }

        [Fact]
        public void DriveTimeKeepsHighNibbleWhenNoFlagBitIsSet()
        {
            var transition = TransitionBuilder.Build(0xFC00, flags: 0x12, driveLow: 0x34);

            Assert.False(transition.Error);
            Assert.False(transition.AutoClose);
            Assert.Equal(0x1234, transition.DriveTime);
        }

        [Fact]
        public void DriveTimeDropsHighNibbleWhenFlagBitIsSet()
        {
            var transition = TransitionBuilder.Build(0xFC00, flags: 0x52, driveLow: 0x34);

            Assert.True(transition.AutoClose);
            Assert.Equal(0x0234, transition.DriveTime);
        }

        [Fact]
        public void HcpExposesOptionRelaisLightBarrierAndLearned()
        {
            var transition = TransitionBuilder.Build(0x0108, hcp1: 0x0C, hcp2: 0x02);

            Assert.True(transition.Hcp.OptionRelais);
            Assert.True(transition.Hcp.LightBarrier);
            Assert.True(transition.Hcp.Learned);
            Assert.False(transition.Hcp.PositionOpen);
            Assert.False(transition.Hcp.PositionClose);
            Assert.False(transition.Hcp.NotReferenced);
        }

        [Fact]
        public void ExposesParseTime()
        {
            DateTime before = DateTime.Now;

            var transition = TransitionBuilder.Build(0x0108);

            Assert.InRange(transition.Time, before, DateTime.Now);
        }
    }
}
