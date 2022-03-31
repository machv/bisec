namespace BiSec.Library.Models
{
    public class User
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public bool IsAdmin { get; set; }
        public int[] Groups { get; set; }

        public override string ToString()
        {
            return $"User {{Id = {Id}, Name = {Name}, IsAdmin = {IsAdmin} }}";
        }
    }
}
