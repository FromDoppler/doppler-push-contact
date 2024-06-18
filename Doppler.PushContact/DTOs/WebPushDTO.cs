using System;

namespace Doppler.PushContact.DTOs
{
    public class WebPushDTO
    {
        public string Title { get; set; }

        public string Body { get; set; }

        public string OnClickLink { get; set; }

        public string ImageUrl { get; set; }

        public Guid MessageId { get; set; }
    }
}
