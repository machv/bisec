using BiSec.Library;
using Xunit;

namespace tests
{
    public class TransitionHelperTests
    {
        [Theory]
        [InlineData(0, ActorState.Closed)]
        [InlineData(0.5, ActorState.Half3)]
        [InlineData(33, ActorState.Half3)]
        [InlineData(33.5, ActorState.Half2)]
        [InlineData(66, ActorState.Half2)]
        [InlineData(66.5, ActorState.Half1)]
        [InlineData(99.5, ActorState.Half1)]
        [InlineData(100, ActorState.Open)]
        [InlineData(100.5, ActorState.Closed)]
        public void BucketsPercentInFiveSteps(double percent, ActorState expected)
        {
            Assert.Equal(expected, TransitionHelper.GetActorStateFromPercent(percent));
        }

        [Fact]
        public void ActorStateHalvesActualAndTreatsMissingTransitionAsClosed()
        {
            Assert.Equal(ActorState.Half3, TransitionHelper.GetActorState(TransitionBuilder.Build(0xFC00, actual: 66)));
            Assert.Equal(ActorState.Half2, TransitionHelper.GetActorState(TransitionBuilder.Build(0xFC00, actual: 67)));
            Assert.Equal(ActorState.Closed, TransitionHelper.GetActorState(null));
        }

        [Fact]
        public void EndPositionsComeFromHcpWhenPresent()
        {
            // actual says open, the HCP block says closed.
            var transition = TransitionBuilder.Build(0x0108, actual: 200, desired: 200, hcp1: 0x02);

            Assert.True(TransitionHelper.IsClosed(transition));
            Assert.False(TransitionHelper.IsOpened(transition));
        }

        [Fact]
        public void EndPositionsComeFromActualWithoutHcp()
        {
            var transition = TransitionBuilder.Build(0xFC00, actual: 200, desired: 200);

            Assert.True(TransitionHelper.IsOpened(transition));
            Assert.False(TransitionHelper.IsClosed(transition));
        }

        [Fact]
        public void DefinedHalfOpenedIsActual100ForEseAndTheHcpFlagOtherwise()
        {
            Assert.True(TransitionHelper.IsDefinedHalfOpened(TransitionBuilder.Build(0x0902, actual: 100, desired: 100)));
            Assert.False(TransitionHelper.IsDefinedHalfOpened(TransitionBuilder.Build(0x0902, actual: 60, desired: 60, hcp1: 0x80)));
            Assert.True(TransitionHelper.IsDefinedHalfOpened(TransitionBuilder.Build(0x0108, actual: 60, desired: 60, hcp1: 0x80)));
            Assert.False(TransitionHelper.IsDefinedHalfOpened(TransitionBuilder.Build(0xFC00, actual: 100, desired: 100)));
        }

        [Fact]
        public void HalfOpenedIsTheHcpFlagOrAnyIntermediatePositionWithoutHcp()
        {
            Assert.False(TransitionHelper.IsHalfOpened(TransitionBuilder.Build(0x0108, actual: 100, desired: 100)));
            Assert.True(TransitionHelper.IsHalfOpened(TransitionBuilder.Build(0x0108, hcp1: 0x80)));
            Assert.True(TransitionHelper.IsHalfOpened(TransitionBuilder.Build(0xFC00, actual: 1, desired: 1)));
            Assert.False(TransitionHelper.IsHalfOpened(TransitionBuilder.Build(0xFC00, actual: 200, desired: 200)));
        }

        [Fact]
        public void ErrorComesFromTransitionFlagOrHcpAndHintFromHcpNotReferenced()
        {
            Assert.True(TransitionHelper.HasError(TransitionBuilder.Build(0xFC00, flags: 0x80)));
            Assert.True(TransitionHelper.HasError(TransitionBuilder.Build(0x0108, hcp1: 0x10)));
            Assert.False(TransitionHelper.HasError(TransitionBuilder.Build(0x0108, hcp2: 0x04)));

            Assert.True(TransitionHelper.HasHint(TransitionBuilder.Build(0x0108, hcp2: 0x04)));
            Assert.False(TransitionHelper.HasHint(TransitionBuilder.Build(0xFC00)));
        }
    }
}
