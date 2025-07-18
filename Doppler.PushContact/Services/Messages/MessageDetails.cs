using System;

namespace Doppler.PushContact.Services.Messages
{
    public class MessageDetails
    {
        public Guid MessageId { get; set; }

        public string Domain { get; set; }

        public string Title { get; set; }

        public string Body { get; set; }

        public string OnClickLink { get; set; }

        public int Sent { get; set; }

        public int Delivered { get; set; }

        public int NotDelivered { get; set; }

        public string ImageUrl { get; set; }
    }
}
