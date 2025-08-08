using Doppler.PushContact.Models.DTOs;
using System.Threading.Tasks;

namespace Doppler.PushContact.Services
{
    public interface IDomainService
    {
        Task UpsertAsync(DomainDTO domain);
        Task<DomainDTO> GetByNameAsync(string name);
        Task<ContactsStatsDTO> GetDomainContactStatsAsync(string name);
    }
}
