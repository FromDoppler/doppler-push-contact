namespace Doppler.PushContact.Models.Models
{
    public class ContactsStats
    {
        public int Deleted { get; set; }
        public int Active { get; set; }
        public int Total { get; set; }
    }

    public class DomainStats
    {
        public string Name { get; set; }
        public ContactsStats ContactsStats { get; set; }
    }
}
