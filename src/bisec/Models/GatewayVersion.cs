namespace BiSec.Library.Models
{
    /// <summary>
    /// Gateway firmware version. The legacy app orders versions by the last two digits of the name.
    /// </summary>
    public class GatewayVersion
    {
        public const int NumberEe001425_06OrLower = 6;
        public const int NumberEe001425_10 = 10;

        /// <summary>
        /// Firmware that does not know GET_GW_VERSION, which was added after EE001425-06.
        /// </summary>
        public static readonly GatewayVersion Ee001425_06OrLower = new GatewayVersion("<=EE001425-06", NumberEe001425_06OrLower);

        public string Name { get; }
        public int Number { get; }

        public GatewayVersion(string name, int number)
        {
            Name = name;
            Number = number;
        }

        public static GatewayVersion FromName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return Ee001425_06OrLower;

            string trimmed = name.Trim();
            string tail = trimmed.Length > 2 ? trimmed.Substring(trimmed.Length - 2) : trimmed;
            int.TryParse(tail, out int number);

            return new GatewayVersion(trimmed, number);
        }

        public override string ToString()
        {
            return Name;
        }
    }
}
