using Doppler.PushContact.Models;
using System.Threading.Tasks;

namespace Doppler.PushContact.Repositories.Interfaces
{
    public interface IDomainRepository
    {
        Task UpsertAsync(Domain domain);
        Task<Domain> GetByNameAsync(string name);
    }
}
