using System;
using System.Collections.Generic;

namespace Doppler.PushContact.Models.DTOs
{
    public class WebPushDTO
    {
        public string Title { get; set; }

        public string Body { get; set; }

        public string OnClickLink { get; set; }

        public string ImageUrl { get; set; }

        public Guid MessageId { get; set; }
        public string Domain { get; set; }
        public string IconUrl { get; set; }
        public bool PreferLargeImage { get; set; }
        public List<MessageActionDTO> Actions { get; set; }
    }
}
