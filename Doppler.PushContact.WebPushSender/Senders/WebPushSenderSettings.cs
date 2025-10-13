namespace Doppler.PushContact.WebPushSender.Senders
{
    public class WebPushSenderSettings
    {
        public string QueueName { get; set; }
        public WebPushSenderTypes Type { get; set; }
        public string PushApiUrl { get; set; }
        public string ActionClickEventEndpointPath { get; set; }
        public string PushContactApiUrl { get; set; }
    }
}
