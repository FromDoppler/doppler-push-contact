using Doppler.PushContact.Models.DTOs;
using Doppler.PushContact.Models.Entities;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Doppler.PushContact.Services
{
    public interface IMessageStatsService
    {
        Task RegisterMessageStatsAsync(IEnumerable<WebPushEvent> webPushEvents);
        Task<MessageStatsDTO> GetMessageStatsAsync(
            string domain,
            Guid? messageId,
            DateTimeOffset dateFrom,
            DateTimeOffset dateTo
        );
    }
}
