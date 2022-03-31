namespace BiSec.Library
{
    public class MessageChecksum
    {
        private Message _message;

        public MessageChecksum(Message message)
        {
            _message = message;
        }

        public int Calculate()
        {
            int hexInt = StringHelper.ToHexInt(_message.Token);

            int value = _message.GetLength();
            value += _message.Tag;
            value += (hexInt & 255);
            value += (hexInt >> 8 & 255);
            value += (hexInt >> 16 & 255);
            value += (hexInt >> 24 & 255);
            value += _message.GetCommandCode();
            foreach (byte b in _message.Payload.ToByteArray())
                value += b;

            value = value & 255;
            return value;
        }
    }
}
