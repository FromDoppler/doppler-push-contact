namespace Doppler.PushContact.Models.DTOs
{
    public class ContactsStatsDTO
    {
        public string DomainName { get; set; }
        public int Deleted { get; set; }
        public int Active { get; set; }
        public int Total { get; set; }
    }
}
