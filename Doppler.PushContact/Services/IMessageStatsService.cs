using Doppler.PushContact.Models.Entities;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Doppler.PushContact.Services
{
    public interface IMessageStatsService
    {
        Task RegisterMessageStatsAsync(IEnumerable<WebPushEvent> webPushEvents);
    }
}
