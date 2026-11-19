using BiSec.Library;
using BiSec.Library.Models;
using Xunit;

namespace tests
{
    public class ActorStateInterpreterTests
    {
        private const int Unknown = 0x0108;
        private const int NoHcp = 0xFC00;
        private const int Ese = 0x0902;
        private const int HetS = 0x0905;
        private const int HetE1 = 0x0908;
        private const int HetE2 = 0x0909;
        private const int HetE2Sl = 0x090D;
        private const int Portamatic = 0x0701;
        private const int Smartkey = 0x0604;

        private static ActorStateInfo Interpret(Transition transition, GroupType groupType = GroupType.SectionalDoor)
        {
            return ActorStateInterpreter.Interpret(transition, groupType);
        }

        [Theory]
        [InlineData(0x0902, ActorClass.Ese, StateHelperKind.Ese)]
        [InlineData(0x0908, ActorClass.HetE1, StateHelperKind.HetE1)]
        [InlineData(0x0909, ActorClass.HetE2, StateHelperKind.HetE2)]
        [InlineData(0x0905, ActorClass.HetS, StateHelperKind.HetS)]
        [InlineData(0x090D, ActorClass.HetE2Sl, StateHelperKind.HetE2Sl)]
        [InlineData(0x0701, ActorClass.Portamatic, StateHelperKind.Portamatic)]
        [InlineData(0x0401, ActorClass.Fes1, StateHelperKind.Fes)]
        [InlineData(0x0402, ActorClass.Fes1_1, StateHelperKind.Fes)]
        [InlineData(0x011B, ActorClass.VersamaticGelenkarmDouble, StateHelperKind.VersamaticDouble)]
        [InlineData(0x011F, ActorClass.VersamaticGleitschieneDouble, StateHelperKind.VersamaticDouble)]
        [InlineData(0x0604, ActorClass.SmartKey, StateHelperKind.Smartkey)]
        [InlineData(0x011A, ActorClass.VersamaticGelenkarmSingle, StateHelperKind.Default)]
        [InlineData(0x011E, ActorClass.VersamaticGleitschieneSingle, StateHelperKind.Default)]
        [InlineData(0x0108, ActorClass.Unknown, StateHelperKind.Default)]
        [InlineData(0xFC00, ActorClass.Undefined, StateHelperKind.Default)]
        [InlineData(0xFFFF, ActorClass.Undefined, StateHelperKind.Default)]
        public void DispatchesByGk(int gk, ActorClass expectedClass, StateHelperKind expectedHelper)
        {
            var transition = TransitionBuilder.Build(gk);

            Assert.Equal(expectedClass, transition.ActorClass);
            Assert.Equal(expectedHelper, ActorStateInterpreter.GetStateHelper(transition));
            Assert.Equal(expectedHelper, Interpret(transition).Helper);
        }

        [Fact]
        public void MissingTransitionIsNoDataAndUnavailable()
        {
            var info = Interpret(null);

            Assert.Equal(StateHelperKind.Default, info.Helper);
            Assert.False(info.IsAvailable);
            Assert.False(info.ShowState);
            Assert.Equal(ActorState.Closed, info.State);
            Assert.Equal(StatusMessage.NoData, info.Message);
            Assert.Equal(StatusMessage.None, info.ValueMessage);
        }

        [Fact]
        public void DefaultClosedByHcp()
        {
            var info = Interpret(TransitionBuilder.Build(Unknown, hcp1: 0x02));

            Assert.True(info.IsAvailable);
            Assert.True(info.ShowState);
            Assert.Equal(ActorState.Closed, info.State);
            Assert.Equal(StatusMessage.Closed, info.Message);
            Assert.Null(info.PositionPercent);
            Assert.Null(info.Lock);
            Assert.Null(info.IsOn);
            Assert.True(info.ShowDrivingDirection);
        }

        [Fact]
        public void DefaultIntermediatePositionPrintsPercent()
        {
            var info = Interpret(TransitionBuilder.Build(Unknown, actual: 25, desired: 25));

            Assert.Equal(ActorState.Half3, info.State);
            Assert.Equal(StatusMessage.Half, info.Message);
            Assert.Equal(12.5, info.PositionPercent);
            Assert.False(info.IsDefinedHalfOpen);
        }

        [Fact]
        public void DefaultDefinedHalfOpenAddsMarker()
        {
            var info = Interpret(TransitionBuilder.Build(Unknown, actual: 100, desired: 100, hcp1: 0x80));

            Assert.Equal(ActorState.Half2, info.State);
            Assert.Equal(StatusMessage.Half, info.Message);
            Assert.Equal(50, info.PositionPercent);
            Assert.True(info.IsDefinedHalfOpen);
        }

        [Fact]
        public void DefaultNotReferencedHidesState()
        {
            var info = Interpret(TransitionBuilder.Build(Unknown, actual: 100, desired: 100, hcp2: 0x04));

            Assert.True(info.NotReferenced);
            Assert.False(info.HasError);
            Assert.False(info.ShowState);
            Assert.Equal(StatusMessage.NotReferenced, info.Message);
            Assert.Null(info.PositionPercent);
        }

        [Theory]
        [InlineData(0x80, 0x00)]
        [InlineData(0x00, 0x10)]
        public void DefaultErrorFromTransitionFlagOrHcp(int flags, int hcp1)
        {
            var info = Interpret(TransitionBuilder.Build(Unknown, actual: 100, desired: 100, flags: flags, hcp1: hcp1));

            Assert.True(info.HasError);
            Assert.False(info.ShowState);
            Assert.Equal(StatusMessage.Error, info.Message);
            Assert.Null(info.PositionPercent);
        }

        [Fact]
        public void DefaultAutoCloseKeepsStateVisibleAndReadsOpened()
        {
            var info = Interpret(TransitionBuilder.Build(Unknown, actual: 200, desired: 200, flags: 0x40, hcp1: 0x01));

            Assert.True(info.AutoClose);
            Assert.True(info.ShowState);
            Assert.Equal(ActorState.Open, info.State);
            Assert.Equal(StatusMessage.Opened, info.Message);
        }

        [Fact]
        public void DefaultDrivingHidesStateAndMessage()
        {
            var info = Interpret(TransitionBuilder.Build(Unknown, actual: 100, desired: 0, hcp1: 0x60));

            Assert.True(info.IsDriving);
            Assert.Equal(DriveDirection.TO_CLOSE, info.DrivingDirection);
            Assert.True(info.ShowDrivingDirection);
            Assert.False(info.ShowState);
            Assert.Equal(StatusMessage.None, info.Message);
            Assert.Null(info.PositionPercent);
        }

        [Fact]
        public void DefaultWithoutHcpShowsNoMessageUntilActualReachesDesired()
        {
            var info = Interpret(TransitionBuilder.Build(NoHcp, actual: 50, desired: 200));

            Assert.False(info.IsDriving);
            Assert.Equal(ActorState.Half3, info.State);
            Assert.Equal(StatusMessage.None, info.Message);
            Assert.Equal(25, info.PositionPercent);
        }

        [Theory]
        [InlineData(GroupType.Light)]
        [InlineData(GroupType.Other)]
        [InlineData(GroupType.Jack)]
        public void LightLikeGroupsReportOnAndOff(GroupType groupType)
        {
            var on = Interpret(TransitionBuilder.Build(Unknown), groupType);
            var off = Interpret(TransitionBuilder.Build(Unknown, actual: 100, desired: 100), groupType);

            Assert.True(on.IsOn);
            Assert.Equal(StatusMessage.On, on.Message);
            Assert.False(off.IsOn);
            Assert.Equal(StatusMessage.Off, off.Message);
            Assert.Null(off.PositionPercent);
        }

        [Fact]
        public void LightWithoutHcpKeepsDoorMessage()
        {
            // The app only picks GENERAL_ON / GENERAL_OFF inside its "hcp present" branch.
            var info = Interpret(TransitionBuilder.Build(NoHcp), GroupType.Light);

            Assert.True(info.IsOn);
            Assert.Equal(StatusMessage.Closed, info.Message);
        }

        [Fact]
        public void EsePrintsHalfMarkerWithoutPercent()
        {
            var info = Interpret(TransitionBuilder.Build(Ese, actual: 100, desired: 100));

            Assert.True(info.IsDefinedHalfOpen);
            Assert.Null(info.PositionPercent);
            Assert.Equal(StatusMessage.Half, info.Message);
            Assert.Equal(ActorState.Half2, info.State);
        }

        [Fact]
        public void HetSNeverPrintsPercent()
        {
            var info = Interpret(TransitionBuilder.Build(HetS, actual: 100, desired: 100));

            Assert.Null(info.PositionPercent);
            Assert.Equal(StatusMessage.None, info.ValueMessage);
            Assert.Equal(StatusMessage.Half, info.Message);
        }

        [Theory]
        [InlineData(1, LockState.Locked, true)]
        [InlineData(2, LockState.Unlocked, true)]
        [InlineData(0, LockState.Error, false)]
        [InlineData(3, LockState.Error, false)]
        public void HetE1LockStateFromInputsInExst5(int exst5, LockState expectedLock, bool expectedShowState)
        {
            var info = Interpret(TransitionBuilder.Build(HetE1, exst5: exst5));

            Assert.Equal(expectedLock, info.Lock);
            Assert.Equal(expectedShowState, info.ShowState);
        }

        [Theory]
        [InlineData(2, true, StatusMessage.PermanentUnlocked)]
        [InlineData(1, false, StatusMessage.PermanentLocked)]
        public void HetE1PermanentUnlockFromRelay2InExst7(int exst7, bool expected, StatusMessage expectedMessage)
        {
            var info = Interpret(TransitionBuilder.Build(HetE1, exst5: 1, exst7: exst7));

            Assert.Equal(expected, info.PermanentUnlock);
            Assert.Equal(expectedMessage, info.Message);
        }

        [Theory]
        [InlineData(0, StatusMessage.Locked)]
        [InlineData(200, StatusMessage.Unlocked)]
        [InlineData(100, StatusMessage.Error)]
        public void HetE1ValueMessageFromActual(int actual, StatusMessage expected)
        {
            var info = Interpret(TransitionBuilder.Build(HetE1, actual: actual, desired: actual, exst5: 1));

            Assert.Equal(expected, info.ValueMessage);
            Assert.Null(info.PositionPercent);
        }

        [Fact]
        public void HetE2IgnoresHcpForStateAndMessage()
        {
            // HCP claims "open"; the helper drops the HCP block and goes by actual.
            var info = Interpret(TransitionBuilder.Build(HetE2, hcp1: 0x01));

            Assert.True(info.ShowState);
            Assert.Equal(ActorState.Closed, info.State);
            Assert.Equal(StatusMessage.Closed, info.Message);
        }

        [Fact]
        public void HetE2IntermediatePositionIsHalfMoving()
        {
            var info = Interpret(TransitionBuilder.Build(HetE2, actual: 20, desired: 20));

            Assert.Equal(ActorState.Half2, info.State);
            Assert.Equal(StatusMessage.HalfMoving, info.Message);
            Assert.Null(info.PositionPercent);
        }

        [Fact]
        public void HetE2KeepsHcpWhenAnOverlayHidesTheState()
        {
            var info = Interpret(TransitionBuilder.Build(HetE2, hcp2: 0x04));

            Assert.False(info.ShowState);
            Assert.Equal(StatusMessage.NotReferenced, info.Message);
        }

        [Theory]
        [InlineData(2, BollardState.Retracted, ActorState.Open, StatusMessage.Retracted)]
        [InlineData(1, BollardState.Extended, ActorState.Closed, StatusMessage.Extended)]
        [InlineData(0, BollardState.NotRetracted, ActorState.Half2, StatusMessage.NotRetracted)]
        [InlineData(3, BollardState.Extended, ActorState.Closed, StatusMessage.Error)]
        public void HetE2SlReadsPinsFromExst5(int exst5, BollardState expectedBollard, ActorState expectedState, StatusMessage expectedMessage)
        {
            var info = Interpret(TransitionBuilder.Build(HetE2Sl, actual: 20, desired: 20, exst5: exst5));

            Assert.Equal(expectedBollard, info.Bollard);
            Assert.Equal(expectedState, info.State);
            Assert.Equal(expectedMessage, info.Message);
            Assert.False(info.ShowDrivingDirection);
            Assert.Null(info.PositionPercent);
        }

        [Theory]
        [InlineData(1, 1, LockState.Locked, StatusMessage.Locked)]
        [InlineData(1, 2, LockState.Unlocked, StatusMessage.Unlocked)]
        [InlineData(2, 1, LockState.Locked, StatusMessage.Locked)]
        [InlineData(2, 0, LockState.Unlocked, StatusMessage.Unlocked)]
        [InlineData(3, 0, LockState.Locked, StatusMessage.Locked)]
        [InlineData(3, 2, LockState.Unlocked, StatusMessage.Unlocked)]
        public void PortamaticLockModesWhenClosed(int exst6, int exst5, LockState expectedLock, StatusMessage expectedValue)
        {
            var info = Interpret(TransitionBuilder.Build(Portamatic, hcp1: 0x02, exst5: exst5, exst6: exst6));

            Assert.Equal(expectedLock, info.Lock);
            Assert.Equal(expectedValue, info.ValueMessage);
            Assert.Equal(StatusMessage.Closed, info.Message);
            Assert.True(info.ShowState);
            Assert.Null(info.StopActive);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(3)]
        public void PortamaticBoltChangerWithInconsistentInputsIsError(int exst5)
        {
            var info = Interpret(TransitionBuilder.Build(Portamatic, hcp1: 0x02, exst5: exst5, exst6: 1));

            Assert.Equal(LockState.Error, info.Lock);
            Assert.Equal(StatusMessage.Error, info.Message);
            Assert.Equal(StatusMessage.None, info.ValueMessage);
            Assert.False(info.ShowState);
        }

        [Fact]
        public void PortamaticLockStateOnlyAppliesWhenClosed()
        {
            var info = Interpret(TransitionBuilder.Build(Portamatic, actual: 200, desired: 200, hcp1: 0x01, exst5: 1, exst6: 1));

            Assert.Null(info.Lock);
            Assert.Equal(StatusMessage.None, info.ValueMessage);
            Assert.Equal(StatusMessage.Opened, info.Message);
        }

        [Theory]
        [InlineData(4, 1, true)]
        [InlineData(4, 0, false)]
        [InlineData(5, 0, true)]
        [InlineData(5, 2, false)]
        public void PortamaticStopModes(int exst6, int exst5, bool expectedStopActive)
        {
            var info = Interpret(TransitionBuilder.Build(Portamatic, actual: 100, desired: 100, exst5: exst5, exst6: exst6));

            Assert.Equal(expectedStopActive, info.StopActive);
            Assert.Equal(expectedStopActive ? StatusMessage.StopActive : StatusMessage.None, info.ValueMessage);
            Assert.Equal(50, info.PositionPercent);
            Assert.Null(info.Lock);
        }

        [Fact]
        public void PortamaticStopActiveAtEndPositionPrintsNoPercent()
        {
            var info = Interpret(TransitionBuilder.Build(Portamatic, hcp1: 0x02, exst5: 1, exst6: 4));

            Assert.Equal(StatusMessage.StopActive, info.ValueMessage);
            Assert.Null(info.PositionPercent);
        }

        [Fact]
        public void PortamaticWithoutConfigurationBehavesLikeDefault()
        {
            var info = Interpret(TransitionBuilder.Build(Portamatic, hcp1: 0x02, exst5: 1, exst6: 0));

            Assert.Null(info.Lock);
            Assert.Null(info.StopActive);
            Assert.Equal(StatusMessage.Closed, info.Message);
        }

        [Fact]
        public void PortamaticAutoCloseMessageFollowsActual()
        {
            // The default helper would read "opened" for any auto-close transition.
            var info = Interpret(TransitionBuilder.Build(Portamatic, flags: 0x40, hcp1: 0x02));

            Assert.True(info.AutoClose);
            Assert.Equal(StatusMessage.Closed, info.Message);
        }

        [Theory]
        [InlineData(0x011B)]
        [InlineData(0x011F)]
        public void VersamaticDoubleReadsOneLeafEachFromExst5AndExst4(int gk)
        {
            var info = Interpret(TransitionBuilder.Build(gk, exst4: 50, exst5: 200), GroupType.SwingGateDouble);

            Assert.Equal(ActorState.Open, info.State);
            Assert.Equal(ActorState.Half3, info.SecondLeafState);
        }

        [Fact]
        public void VersamaticSingleUsesActual()
        {
            var info = Interpret(TransitionBuilder.Build(0x011A, exst4: 50, exst5: 200), GroupType.SwingGateSingle);

            Assert.Equal(ActorState.Closed, info.State);
            Assert.Null(info.SecondLeafState);
        }

        [Theory]
        [InlineData(0, LockState.Error, StatusMessage.NotReferenced, ActorState.Open, false)]
        [InlineData(1, LockState.Unlocked, StatusMessage.Opened, ActorState.Half3, true)]
        [InlineData(2, LockState.Unlocked, StatusMessage.Unlocked, ActorState.Closed, true)]
        [InlineData(3, LockState.Locked, StatusMessage.Locked, ActorState.Closed, true)]
        [InlineData(4, LockState.Locked, StatusMessage.Locked, ActorState.Closed, true)]
        [InlineData(5, LockState.Locked, StatusMessage.Locked, ActorState.Closed, true)]
        [InlineData(18, null, StatusMessage.Opened, ActorState.Open, true)]
        public void SmartkeyStateFromExst7(int exst7, LockState? expectedLock, StatusMessage expectedMessage, ActorState expectedState, bool expectedShowState)
        {
            var info = Interpret(TransitionBuilder.Build(Smartkey, actual: 200, desired: 200, exst7: exst7), GroupType.Smartkey);

            Assert.True(info.IsAvailable);
            Assert.Equal(expectedLock, info.Lock);
            Assert.Equal(expectedMessage, info.Message);
            Assert.Equal(expectedState, info.State);
            Assert.Equal(expectedShowState, info.ShowState);
            Assert.Equal(BatteryState.Ok, info.Battery);
            Assert.False(info.ShowDrivingDirection);
        }

        [Theory]
        [InlineData(1, BatteryState.Low)]
        [InlineData(2, BatteryState.Empty)]
        public void SmartkeyBatteryFromExst5ForcesClosedState(int exst5, BatteryState expected)
        {
            var info = Interpret(TransitionBuilder.Build(Smartkey, exst5: exst5, exst7: 1), GroupType.Smartkey);

            Assert.Equal(expected, info.Battery);
            Assert.Equal(ActorState.Closed, info.State);
            Assert.Equal(LockState.Unlocked, info.Lock);
        }

        [Fact]
        public void SmartkeyWithoutHcpIsUnavailable()
        {
            // The forecast-lead-time flag makes the parser drop the HCP block.
            var transition = TransitionBuilder.Build(Smartkey, hcp2: 0x01, exst7: 3);
            var info = Interpret(transition, GroupType.Smartkey);

            Assert.Null(transition.Hcp);
            Assert.False(info.IsAvailable);
            Assert.False(info.ShowState);
            Assert.Null(info.Lock);
            Assert.Null(info.Battery);
            Assert.Equal(StatusMessage.None, info.Message);
        }
    }
}
