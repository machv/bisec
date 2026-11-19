using BiSec.Library;
using System.Linq;
using System.Reflection;
using Xunit;

namespace tests
{
    public class CommandTests
    {
        [Fact]
        public void EveryCodeMapsBackToItsOwnInstance()
        {
            Assert.All(Command.Commands, command => Assert.Same(command, Command.GetCommand(command.Code)));
        }

        [Fact]
        public void NoCodeAndNoNameIsUsedTwice()
        {
            Assert.Empty(Command.Commands.GroupBy(c => c.Code).Where(g => g.Count() > 1).Select(g => g.Key));
            Assert.Empty(Command.Commands.GroupBy(c => c.Name).Where(g => g.Count() > 1).Select(g => g.Key));
        }

        [Fact]
        public void EveryDeclaredCommandIsRegistered()
        {
            // A command missing from the list would be decoded as "UNKNOWN" and never match its static instance.
            var declared = typeof(Command)
                .GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(f => f.FieldType == typeof(Command))
                .Select(f => (Command)f.GetValue(null))
                .ToArray();

            Assert.NotEmpty(declared);
            Assert.All(declared, command => Assert.Contains(command, Command.Commands));
        }

        [Fact]
        public void CodeFromTheWireWithTheResponseBitRemovedFindsTheCommand()
        {
            // Length, tag, token, command, checksum; 0xF0 is HM_GET_TRANSITION (112) with bit 7 set.
            Message message = Message.From("0009" + "00" + "00000000" + "F0" + "00");

            Assert.Same(Command.GetTransition, message.Command);
            Assert.True(message.IsResponse);
        }
    }
}
