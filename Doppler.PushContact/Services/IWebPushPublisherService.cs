using Doppler.PushContact.Models.DTOs;
using System.Threading;

namespace Doppler.PushContact.Services
{
    public interface IWebPushPublisherService
    {
        void ProcessWebPush(string domain, WebPushDTO webPushDTO, string authenticationApiToken = null);
        void ProcessWebPushInBatches(string domain, WebPushDTO messageDTO, string authenticationApiToken = null);
    }
}
