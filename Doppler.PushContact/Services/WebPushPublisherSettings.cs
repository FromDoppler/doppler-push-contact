using System.Collections.Generic;

namespace Doppler.PushContact.Services
{
    public class WebPushPublisherSettings
    {
        public Dictionary<string, List<string>> PushEndpointMappings { get; set; }
        public string ClickedEventEndpointPath { get; set; }
        public string ReceivedEventEndpointPath { get; set; }
        public string PushContactApiUrl { get; set; }
        public int ProcessPushBatchSize { get; set; }
    }
}
