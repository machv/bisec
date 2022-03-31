using BiSec.Library.Exceptions;
using System;
using System.IO;
using System.Text;

namespace BiSec.Library
{
    public class Package
    {
        private byte _checksum;

        private string _sender;
        public string Sender
        {
            get => _sender;
            set => _sender = value;
        }

        private string _receiver;
        public string Receiver => _receiver;

        private Message _message;
        public Message Message => _message;

        protected byte CalculatedChecksum => PackageChecksum.Calculate(this);
        public bool IsCorrectChecksum => _checksum == CalculatedChecksum;

        public Package(string sender, string receiver, Message message)
        {
            _sender = sender;
            _receiver = receiver;
            _message = message;
            _checksum = PackageChecksum.Calculate(this);
        }

        public Package(string sender, string receiver, Message message, byte checksum)
        {
            _sender = sender;
            _receiver = receiver;
            _message = message;
            _checksum = checksum;
        }

        public byte[] ToByteArray()
        {
            using MemoryStream stream = new MemoryStream();
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                writer.Write(_sender.ToHexByteArray());
                writer.Write(_receiver.ToHexByteArray());
                writer.Write(_message.ToBytes());
                writer.Write(_checksum);

                return stream.ToArray();
            }
        }

        public static Package Load(byte[] bytes)
        {
            if (bytes.Length < Lengths.MinimalPackageBytes)
                throw new InvalidPackageLengthException("Wrong size: " + StringHelper.HexStringFromByteArray(bytes));

            var message = Encoding.ASCII.GetString(bytes);

            int offset = 0;
            var sender = message.Substring(offset, Lengths.ADDRESS_BYTES * 2);
            offset += Lengths.ADDRESS_BYTES * 2;

            var receiver = message.Substring(offset, Lengths.ADDRESS_BYTES * 2);
            offset += Lengths.ADDRESS_BYTES * 2;

            var messageData = message.Substring(offset, message.Length - offset - Lengths.CHECKSUM_BYTES * 2);
            offset += message.Length - offset - Lengths.CHECKSUM_BYTES * 2;
            var pack = Message.From(messageData);

            string checksumPart = message.Substring(offset, Lengths.CHECKSUM_BYTES * 2);
            byte[] checksumArray = checksumPart.ToHexByteArray();
            byte checksum = checksumArray[0]; // ba.copyOfRange(ba.size - Lengths.CHECKSUM_BYTES, ba.size).toHexString().toInt(16).toByte();

            return new Package(sender, receiver, pack, checksum);
        }
    }
}
