using Doppler.PushContact.ApiModels;
using Doppler.PushContact.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Doppler.PushContact.Services
{
    public interface IPushContactService
    {
        Task AddAsync(PushContactModel pushContactModel);

        Task UpdateEmailAsync(string deviceToken, string email);

        Task<IEnumerable<PushContactModel>> GetAsync(PushContactFilter pushContactFilter);

        Task<long> DeleteByDeviceTokenAsync(IEnumerable<string> deviceTokens);

        Task AddHistoryEventsAsync(IEnumerable<PushContactHistoryEvent> pushContactHistoryEvents);

        Task<IEnumerable<string>> GetAllDeviceTokensByDomainAsync(string domain);

        Task<ApiPage<DomainInfo>> GetDomains(int page, int per_page);
    }
}
