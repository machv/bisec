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
    }
}
