namespace Doppler.PushContact.Models.Models
{
    public class MessageStatsTotals
    {
        public int Sent { get; set; }
        public int Delivered { get; set; }
        public int Click { get; set; }
        public int Received { get; set; }
        public int NotDelivered { get; set; }
        public int ActionClick { get; set; }
        public int BillableSends { get; set; }
    }
}
