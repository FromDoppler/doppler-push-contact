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
        Task<IEnumerable<SubscriptionInfoDTO>> GetAllSubscriptionInfoByVisitorGuidAsync(string visitorGuid);
        Task<ContactsStatsDTO> GetContactsStatsAsync(string domainName);
    }
}
