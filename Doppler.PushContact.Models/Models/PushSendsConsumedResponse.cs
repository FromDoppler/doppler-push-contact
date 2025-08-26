using System;

namespace Doppler.PushContact.Models.Models
{
    public class PushSendsConsumedResponse
    {
        public string Domain { get; set; }
        public DateTimeOffset From { get; set; }
        public DateTimeOffset To { get; set; }
        public int Consumed { get; set; }
    }
}
