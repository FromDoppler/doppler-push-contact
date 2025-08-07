using Doppler.PushContact.Models;
using Doppler.PushContact.Models.DTOs;
using System.Threading.Tasks;

namespace Doppler.PushContact.Repositories.Interfaces
{
    public interface IDomainRepository
    {
        Task UpsertAsync(DomainDTO domain);
        Task<DomainDTO> GetByNameAsync(string name);
    }
}
