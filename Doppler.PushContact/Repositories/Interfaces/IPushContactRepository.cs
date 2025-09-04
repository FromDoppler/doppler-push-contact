using Doppler.PushContact.ApiModels;
using Doppler.PushContact.DTOs;
using Doppler.PushContact.Models.DTOs;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Doppler.PushContact.Repositories.Interfaces
{
    public interface IPushContactRepository
    {
        Task<IEnumerable<SubscriptionInfoDTO>> GetAllSubscriptionInfoByDomainAsync(string domain);
        IAsyncEnumerable<SubscriptionInfoDTO> GetSubscriptionInfoByDomainAsStreamAsync(string domain, CancellationToken cancellationToken = default);
        Task<IEnumerable<SubscriptionInfoDTO>> GetAllSubscriptionInfoByVisitorGuidAsync(string domain, string visitorGuid);
        Task<ContactsStatsDTO> GetContactsStatsAsync(string domainName);
        Task<VisitorInfoDTO> GetVisitorInfoSafeAsync(string deviceToken);
        Task<ApiPage<string>> GetDistinctVisitorGuidByDomain(string domain, int page, int per_page);
        Task<ApiPage<string>> GetAllVisitorGuidByDomain(string domain, int page, int per_page);
    }
}
