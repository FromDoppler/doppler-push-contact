using Doppler.PushContact.Models.DTOs;
using Doppler.PushContact.Models.Models;

namespace Doppler.PushContact.Services
{
    public interface IWebPushPublisherService
    {
        void ProcessWebPushByDomainInBatches(string domain, WebPushDTO messageDTO, string authenticationApiToken = null);
        void ProcessWebPushForVisitors(WebPushDTO messageDTO, FieldsReplacementList visitorsWithReplacements, string authenticationApiToken = null);
    }
}
