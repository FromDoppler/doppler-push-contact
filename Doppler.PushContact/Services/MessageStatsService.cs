using Doppler.PushContact.Models.Entities;
using Doppler.PushContact.Models.Enums;
using Doppler.PushContact.Repositories.Interfaces;
using Doppler.PushContact.Transversal;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Doppler.PushContact.Services
{
    public class MessageStatsService : IMessageStatsService
    {
        private readonly IMessageStatsRepository _repository;
        private readonly ILogger<MessageStatsService> _logger;

        public MessageStatsService(
            IMessageStatsRepository repository,
            ILogger<MessageStatsService> logger
        )
        {
            _repository = repository;
            _logger = logger;
        }

        public async Task RegisterMessageStatsAsync(IEnumerable<WebPushEvent> webPushEvents)
        {
            if (webPushEvents == null || !webPushEvents.Any())
            {
                return;
            }

            // group by domain + messageId + truncated hour
            var groupedStats = WebPushEventsMapper.MapWebPushEventsToMessageStatsGroups(webPushEvents);

            try
            {
                await _repository.BulkUpsertStatsAsync(groupedStats);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while registering MessageStats for {Count} groups.", groupedStats.Count);
            }
        }
    }
}
