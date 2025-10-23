using Doppler.PushContact.Models.Entities;
using System.Threading.Tasks;

namespace Doppler.PushContact.WebPushSender.Repositories.Interfaces
{
    public interface IMessageStatsRepository
    {
        Task UpsertMessageStatsAsync(MessageStats messageStats);
    }
}
