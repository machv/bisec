using BiSec.Library;
using System;
using Xunit;

namespace tests
{
    public class PayloadTests
    {
        [Fact]
        public void JcmpPayload()
        {
            string command = "{\"CMD\":\"GET_USERS\"}";
            var payload = PayloadFactory.Jmcp(command);

            Assert.Equal("7B22434D44223A224745545F5553455253227D", StringHelper.HexStringFromByteArray(payload.ToByteArray()));
            Assert.Equal(command, payload.ToString());
        }
    }
}
