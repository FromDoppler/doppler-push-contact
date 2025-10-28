using System;

namespace Doppler.PushContact.Models.DTOs
{
    public class MessageStatsDTO
    {
        public string Domain { get; set; }
        public Guid MessageId { get; set; }
        public DateTimeOffset DateFrom { get; set; }
        public DateTimeOffset DateTo { get; set; }
        public int Sent { get; set; }
        public int Delivered { get; set; }
        public int NotDelivered { get; set; }
        public int Received { get; set; }
        public int Click { get; set; }
        public int ActionClick { get; set; }
        public int BillableSends { get; set; }
    }
}
