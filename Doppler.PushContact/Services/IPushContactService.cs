using Doppler.PushContact.ApiModels;
using Doppler.PushContact.DTOs;
using Doppler.PushContact.Models;
using Doppler.PushContact.Models.DTOs;
using Doppler.PushContact.Services.Messages;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Doppler.PushContact.Services
{
    public interface IPushContactService
    {
        Task AddAsync(PushContactModel pushContactModel);

        Task<bool> UpdateSubscriptionAsync(string deviceToken, SubscriptionDTO subscription);

        Task UpdateEmailAsync(string deviceToken, string email);

        Task<IEnumerable<PushContactModel>> GetAsync(PushContactFilter pushContactFilter);

        Task<long> DeleteByDeviceTokenAsync(IEnumerable<string> deviceTokens);

        Task AddHistoryEventsAsync(IEnumerable<PushContactHistoryEvent> pushContactHistoryEvents);

        Task AddHistoryEventsAndMarkDeletedContactsAsync(Guid messageId, SendMessageResult sendMessageResult);

        Task<IEnumerable<string>> GetAllDeviceTokensByDomainAsync(string domain);

        Task<IEnumerable<string>> GetAllDeviceTokensByVisitorGuidAsync(string visitorGuid);

        Task<ApiPage<DomainInfo>> GetDomains(int page, int per_page);

        Task<MessageDeliveryResult> GetDeliveredMessageSummarizationAsync(string domain, Guid messageId, DateTimeOffset from, DateTimeOffset to);

        Task UpdatePushContactVisitorGuid(string deviceToken, string visitorGuid);

        Task<ApiPage<string>> GetAllVisitorGuidByDomain(string domain, int page, int per_page);

        Task<bool> GetEnabledByVisitorGuid(string domain, string visitorGuid);
        Task<string> GetPushContactDomainAsync(string pushContactId);
        Task<VisitorInfoDTO> GetVisitorInfoSafeAsync(string deviceToken);
    }
}
