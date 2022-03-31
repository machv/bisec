using System.Buffers.Binary;
using System.IO;
using System.Net;
using System.Text;

namespace BiSec.Library
{
    public class Message
    {
        public Command Command { get; set; }
        public Payload Payload { get; set; }
        public byte Tag { get; set; }
        public string Token { get; set; } = "00000000";
        public bool IsResponse { get; set; } = false;
        public byte Checksum { get; set; } = 0;

        public byte CalculatedChecksum
        {
            get
            {
                var checksum = new MessageChecksum(this).Calculate();

                return (byte)checksum;
            }
        }

        public Message() { }

        public Message(Command command, Payload payload)
        {
            Command = command;
            Payload = payload;
        }

        public Message(Command command, Payload payload, byte checksum)
        {
            Command = command;
            Payload = payload;
            Checksum = checksum;
        }

        public Message(Command command, byte tag, string token, Payload payload, bool isResponse, byte checksum)
        {
            Command = command;
            Tag = tag;
            Token = token;
            Payload = payload;
            IsResponse = isResponse;
            Checksum = checksum;
        }

        public int GetLength()
        {
            return Lengths.MinimalMessageBytes + Payload.ToByteArray().Length;
        }

        public byte GetCommandCode()
        {
            var code = Command.Code;
            if (IsResponse)
            {
                var flipped = code | (1 << 7);
                return (byte)flipped;
            }

            return (byte)code;
        }

        public string ToHexString()
        {
            byte[] bytes = ToBytes();

            return StringHelper.HexStringFromByteArray(bytes);
        }

        public byte[] ToBytes()
        {
            using (MemoryStream stream = new MemoryStream())
            {
                using (BinaryWriter writer = new BinaryWriter(stream))
                {
                    int length = GetLength();
                    byte[] lengthBytes = ConversionHelper.IntToByteArray(length, 2);

                    writer.Write(lengthBytes);
                    writer.Write(Tag);
                    writer.Write(StringHelper.HexStringToByteArray(Token));
                    writer.Write(GetCommandCode());
                    writer.Write(Payload.ToByteArray());
                    writer.Write(CalculatedChecksum);

                    return stream.ToArray();
                }
            }
        }

        public Error GetError()
        {
            return (Error)Payload.ToByteArray()[0];
        }

        private static bool IsBitSet(short b, int pos)
        {
            return (b & (1 << pos)) != 0;
        }

        public static Message From(string messageData)
        {
            var message = new Message();

            var payload = StringHelper.HexStringToByteArray(messageData);
            using (MemoryStream stream = new MemoryStream(payload))
            {
                using (BinaryReader reader = new BinaryReader(stream))
                {
                    short length = BinaryPrimitives.ReadInt16BigEndian(reader.ReadBytes(2));

                    //short length = reader.ReadInt16();
                    byte tag = reader.ReadByte();
                    var tokenData = reader.ReadBytes(4);
                    string token = StringHelper.HexStringFromByteArray(tokenData);
                    //string token = Encoding.ASCII.GetString(tokenData);
                    byte command = reader.ReadByte();
                    bool isAnswer = false;
                    if (IsBitSet(command, 7))
                    {
                        command = (byte)(command ^ (1 << 7));
                        isAnswer = true;
                    }
                    Command cmd = Command.GetCommand(command);
                    message.Command = cmd;

                    byte[] innerMessage = reader.ReadBytes(payload.Length - 2 - 1 - 4 - 1 - 1); // - length - tag - token - command - checksums
                    byte innerChecksum = reader.ReadByte();
                    //byte outerChecksum = reader.ReadByte();

                    return new Message(cmd, tag, token, new Payload(innerMessage), isAnswer, innerChecksum);
                }
            }
        }

        public static Message CreateMessage(Command command, Payload payload)
        {
            var message = new Message(command, payload);
            var checksum = new MessageChecksum(message);

            return new Message(command, payload, (byte)checksum.Calculate());
        }

        public static Message Jmcp(string content)
        {
            return CreateMessage(Command.Jmcp, PayloadFactory.Jmcp(content));
        }

        public static Message Login(string userName, string password)
        {
            return CreateMessage(Command.Login, PayloadFactory.Login(userName, password));
        }
    }
}
