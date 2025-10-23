using Doppler.PushContact.Models.Entities;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Doppler.PushContact.Repositories.Interfaces
{
    public interface IMessageStatsRepository
    {
        Task BulkUpsertStatsAsync(IEnumerable<MessageStats> stats);
        Task UpsertMessageStatsAsync(MessageStats messageStats);
    }
}
