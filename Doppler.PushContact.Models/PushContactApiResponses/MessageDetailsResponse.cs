using System;

namespace Doppler.PushContact.Models.PushContactApiResponses
{
    public class MessageDetailsResponse
    {
        public string Domain { get; set; }
        public Guid MessageId { get; set; }
        public int Sent { get; set; }
        public int Delivered { get; set; }
        public int NotDelivered { get; set; }
        public int BillableSends { get; set; }
        public int Clicks { get; set; }
        public int Received { get; set; }
    }
}
