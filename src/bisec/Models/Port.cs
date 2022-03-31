namespace BiSec.Library.Models
{
    public class Port
    {
        public int Id { get; set; }
        public PortType Type { get; set; }

        public override string ToString()
        {
            return $"Port {{Id = {Id}, Type = {Type} }}";
        }
    }
}
