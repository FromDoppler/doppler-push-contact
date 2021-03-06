using System;

namespace Doppler.PushContact.Models
{
    public class PushContactHistoryEvent
    {
        public string DeviceToken { get; set; }

        public bool SentSuccess { get; set; }

        public DateTime EventDate { get; set; }

        public string Details { get; set; }

        public Guid MessageId { get; set; }
    }
}
