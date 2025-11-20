using System;

namespace Doppler.PushContact.Models.DTOs
{
    public class MessageStatsPeriodDTO
    {
        public DateTime Date { get; set; }
        public int Sent { get; set; }
        public int Delivered { get; set; }
        public int Click { get; set; }
        public int Received { get; set; }
        public int NotDelivered { get; set; }
        public int ActionClick { get; set; }
        public int BillableSends { get; set; }
    }
}
