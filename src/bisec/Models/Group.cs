namespace BiSec.Library.Models
{
    public class Group
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public GroupType Type { get; set; }
        public Port[] Ports { get; set; }

        public override string ToString()
        {
            return $"Group {{Id = {Id}, Name = {Name}, Type = {Type} }}";
        }
    }
}
