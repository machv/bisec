namespace BiSec.Library.Models
{
    public class Group
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public GroupType Type { get; set; }
        public Port[] Ports { get; set; }

        /// <summary>
        /// The port the gateway is asked for this group's state, or null when the group cannot report it.
        /// </summary>
        public int? RequestablePortId { get; set; }

        public override string ToString()
        {
            return $"Group {{Id = {Id}, Name = {Name}, Type = {Type} }}";
        }
    }
}
