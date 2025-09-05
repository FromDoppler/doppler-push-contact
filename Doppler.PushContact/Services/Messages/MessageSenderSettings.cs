using System.Collections.Generic;

namespace Doppler.PushContact.Services.Messages
{
    public class MessageSenderSettings
    {
        public string PushApiUrl { get; set; }

        public List<int> FatalMessagingErrorCodes { get; set; }

        public int PushTokensLimit { get; set; }

        public string AuthenticationToken { get; set; }
    }
}
