using Doppler.PushContact.DTOs;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Doppler.PushContact.Repositories.Interfaces
{
    public interface IPushContactRepository
    {
        Task<IEnumerable<SubscriptionInfoDTO>> GetAllSubscriptionInfoByDomainAsync(string domain);
    }
}
