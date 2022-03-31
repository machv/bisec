﻿using BiSec.Library;
using BiSec.Library.Models;
using System;
using Xunit;

namespace tests
{
    public class JsonTests
    {
        [Fact]
        public void GetUsersResponse()
        {
            string message = "35343130454338353241333130303030303030303030303630304235303236414242304343323836354237423232363936343232334133303243323236453631364436353232334132323631363436443639364532323243323236393733343136343644363936453232334137343732373536353243323236373732364637353730373332323341354235443744324337423232363936343232334133313243323236453631364436353232334132323733364636323635373336393645323232433232363937333431363436443639364532323341363636313643373336353243323236373732364637353730373332323341354233303243333135443744324337423232363936343232334133323243323236453631364436353232334132323736364336313634363936443639373232303644363136333638323232433232363937333431363436443639364532323341363636313643373336353243323236373732364637353730373332323341354233303243333135443744354439394239";
            byte[] messageBytes = StringHelper.HexStringToByteArray(message);
            
            var tc = Package.Load(messageBytes);

            string json = tc.Message.Payload.TextContent;

            var users = GatewayClient.ParseJsonResponse<User[]>(json);

            Assert.Equal(Command.Jmcp, tc.Message.Command);
            Assert.Equal(3, users.Length);
        }

        [Fact]
        public void GetValuesResponse()
        {
            string message = "5410EC852A3100000000000501CB005D89F13A867B223030223A312C223031223A352C223032223A352C223033223A302C223034223A302C223035223A302C223036223A302C223037223A302C223038223A302C223039223A302C223130223A302C223131223A302C223132223A302C223133223A302C223134223A302C223135223A302C223136223A302C223137223A312C223138223A322C223139223A302C223230223A302C223231223A302C223232223A302C223233223A302C223234223A302C223235223A302C223236223A302C223237223A302C223238223A302C223239223A302C223330223A302C223331223A302C223332223A302C223333223A302C223334223A302C223335223A302C223336223A302C223337223A302C223338223A302C223339223A302C223430223A302C223431223A302C223432223A302C223433223A302C223434223A302C223435223A302C223436223A302C223437223A302C223438223A302C223439223A302C223530223A302C223531223A302C223532223A302C223533223A302C223534223A302C223535223A302C223536223A302C223537223A302C223538223A302C223539223A302C223630223A302C223631223A302C223632223A302C223633223A37397DBE2D";
            //byte[] messageBytes = StringTools.HexStringToByteArray();

            var tc = Package.Load(message.ToByteArray());
            string json = tc.Message.Payload.TextContent;

            var values = GatewayClient.ParseJsonResponse<System.Collections.Generic.Dictionary<string, int>>(json);

            Assert.Equal(64, values.Count);
        }
    }
}
