using BiSec.Harness;
using BiSec.Library;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace tests
{
    /// <summary>
    /// Runs the console harness in-process against the fake gateway. Console output is redirected
    /// process-wide, which is safe because no other test writes to or reads from the console.
    /// </summary>
    public class HarnessTests
    {
        private sealed class Result
        {
            public int ExitCode;
            public string Output;
            public string Error;
        }

        private static async Task<Result> RunAsync(FakeGateway gateway, string input, params string[] args)
        {
            string[] connection =
            {
                "--host", "127.0.0.1",
                "--mac", FakeGateway.GatewayId,
                "--port", gateway.Port.ToString(),
                "--user", GatewayTestSession.AdminName,
                "--password", GatewayTestSession.AdminPassword,
            };

            TextWriter previousOutput = Console.Out;
            TextWriter previousError = Console.Error;
            TextReader previousInput = Console.In;

            StringWriter output = new StringWriter();
            StringWriter error = new StringWriter();
            Console.SetOut(output);
            Console.SetError(error);
            Console.SetIn(new StringReader(input ?? string.Empty));

            try
            {
                int exitCode = await Program.Main(args.Concat(connection).ToArray()).WaitAsync(TimeSpan.FromSeconds(30));

                return new Result { ExitCode = exitCode, Output = output.ToString(), Error = error.ToString() };
            }
            finally
            {
                Console.SetOut(previousOutput);
                Console.SetError(previousError);
                Console.SetIn(previousInput);
            }
        }

        [Fact]
        public async Task GroupsCommandListsGroupsAndLogsOut()
        {
            using var gateway = new FakeGateway();

            Result result = await RunAsync(gateway, null, "groups");

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("'Garage'  type SectionalDoor  requestable port 0  ports [0:Impuls]", result.Output);
            Assert.Contains("'Gate'  type SlidingGate  requestable port 1  ports [1:Impuls, 2:Up]", result.Output);
            Assert.Equal("LOGOUT", gateway.RequestNames.Last());
        }

        [Fact]
        public async Task StateCommandPrintsTheInterpretedState()
        {
            using var gateway = new FakeGateway();
            gateway.SetTransitions(0, FakeGateway.TransitionPayload(200, 200, 0));

            Result result = await RunAsync(gateway, null, "state", "0");

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("actual 200 (100 %)", result.Output);
            Assert.Contains("state Open", result.Output);
        }

        [Fact]
        public async Task ChangingCommandWithoutTheWriteSwitchSendsNothing()
        {
            using var gateway = new FakeGateway();

            Result result = await RunAsync(gateway, null, "remove-user", "3");

            Assert.Equal(2, result.ExitCode);
            Assert.Contains("--write", result.Error);
            Assert.Equal(0, gateway.ConnectionCount);
            Assert.NotNull(gateway.User(GatewayTestSession.UserName));
        }

        [Fact]
        public async Task ChangingCommandIsAbortedWhenTheConfirmationIsNotTyped()
        {
            using var gateway = new FakeGateway();

            Result result = await RunAsync(gateway, "no\n", "remove-user", "3", "--write");

            Assert.Equal(3, result.ExitCode);
            Assert.Equal(0, gateway.ConnectionCount);
            Assert.NotNull(gateway.User(GatewayTestSession.UserName));
        }

        [Fact]
        public async Task ChangingCommandRunsAfterATypedConfirmation()
        {
            using var gateway = new FakeGateway();

            Result result = await RunAsync(gateway, "yes\n", "remove-user", "3", "--write");

            Assert.Equal(0, result.ExitCode);
            Assert.DoesNotContain(gateway.Users, user => user.Name == GatewayTestSession.UserName);
        }

        [Fact]
        public async Task GatewayErrorGivesExitCodeOneAndNamesTheError()
        {
            using var gateway = new FakeGateway();
            gateway.FailNext(Command.RemoveUser, BiSec.Library.Error.UserNotFound);

            Result result = await RunAsync(gateway, null, "remove-user", "3", "--write", "--yes");

            Assert.Equal(1, result.ExitCode);
            Assert.Contains("UserNotFound", result.Error);
        }

        [Fact]
        public async Task PressWatchTriggersThePortAndFollowsUntilSettled()
        {
            using var gateway = new FakeGateway();
            gateway.SetTransitions(0, FakeGateway.TransitionPayload(200, 200, 0), FakeGateway.TransitionPayload(200, 200, 0));

            Result result = await RunAsync(gateway, null, "press-watch", "0", "0", "--write", "--yes");

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("SET_STATE", gateway.RequestNames);
            Assert.Contains("Settled.", result.Output);
        }
    }
}
