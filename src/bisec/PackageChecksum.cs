namespace BiSec.Library
{
    public static class PackageChecksum
    {
        public static byte Calculate(Package package)
        {
            return Calculate(package.Sender, package.Receiver, package.Message);
        }

        public static byte Calculate(string sender, string receiver, Message message)
        {
            string str = sender + receiver + message.ToHexString();

            var value = 0;
            foreach(byte s in str)
                value += s;

            value = value & 255;

            return (byte)value;
        }
    }
}
