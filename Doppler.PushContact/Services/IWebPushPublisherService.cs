using Doppler.PushContact.Models.DTOs;
using Doppler.PushContact.Models.Models;

namespace Doppler.PushContact.Services
{
    public interface IWebPushPublisherService
    {
        void ProcessWebPush(string domain, WebPushDTO webPushDTO, string authenticationApiToken = null);
        void ProcessWebPushInBatches(string domain, WebPushDTO messageDTO, string authenticationApiToken = null);
        void ProcessWebPushForVisitor(string visitorGuid, WebPushDTO messageDTO, MessageReplacements messageReplacements, string authenticationApiToken = null);
    }
}
