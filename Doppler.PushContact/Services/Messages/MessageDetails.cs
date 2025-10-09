using Doppler.PushContact.Models.Entities;
using System;
using System.Collections.Generic;

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
        public int BillableSends { get; set; }
        public int Clicks { get; set; }
        public int Received { get; set; }

        public List<MessageAction> Actions { get; set; }
    }
}
