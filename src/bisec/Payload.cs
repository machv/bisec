using System.Text;

namespace BiSec.Library
{
    public class Payload
    {
        private byte[] _content;
        private PayloadType _type;

        public string TextContent => Encoding.UTF8.GetString(_content);

        public Payload(byte[] content, PayloadType payloadType)
        {
            _content = content;
            _type = payloadType;
        }

        public Payload(byte[] content) : this(content, PayloadType.Mcp) { }

        public byte[] ToByteArray()
        {
            return _content;
        }

        public override string ToString()
        {
            return _type == PayloadType.Mcp ?
                StringHelper.HexStringFromByteArray(_content) :
                TextContent;
        }
    }
}
