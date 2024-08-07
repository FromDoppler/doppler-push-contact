using Doppler.PushContact.Models.DTOs;

namespace Doppler.PushContact.Models
{
    public class PushContactModel
    {
        public string Domain { get; set; }

        public string DeviceToken { get; set; }

        public string Email { get; set; }

        public string VisitorGuid { get; set; }

        public SubscriptionDTO Subscription { get; set; }
    }
}
