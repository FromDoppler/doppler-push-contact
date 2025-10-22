using Doppler.PushContact.Models.Entities;
using Doppler.PushContact.Models.Enums;
using Doppler.PushContact.Repositories.Interfaces;
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
            var groupedStats = webPushEvents
                .GroupBy(e => new
                {
                    e.Domain,
                    e.MessageId,
                    Date = TruncateToHour(e.Date)
                })
                .Select(g => new MessageStats
                {
                    Domain = g.Key.Domain,
                    MessageId = g.Key.MessageId,
                    Date = g.Key.Date,

                    Sent = g.Count(),
                    Delivered = g.Count(x => x.Type == (int)WebPushEventType.Delivered),
                    Received = g.Count(x => x.Type == (int)WebPushEventType.Received),
                    Click = g.Count(x => x.Type == (int)WebPushEventType.Clicked),
                    ActionClick = g.Count(x => x.Type == (int)WebPushEventType.ActionClick),

                    NotDelivered = g.Count(x =>
                        x.Type == (int)WebPushEventType.DeliveryFailed ||
                        x.Type == (int)WebPushEventType.ProcessingFailed),

                    BillableSends = g.Count(x =>
                        x.Type == (int)WebPushEventType.Delivered ||
                        (x.Type == (int)WebPushEventType.DeliveryFailed && x.SubType == (int)WebPushEventSubType.InvalidSubcription))
                })
                .ToList();

            try
            {
                await _repository.BulkUpsertStatsAsync(groupedStats);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while registering MessageStats for {Count} groups.", groupedStats.Count);
            }
        }

        private static DateTime TruncateToHour(DateTime date)
        {
            return new DateTime(date.Year, date.Month, date.Day, date.Hour, 0, 0, DateTimeKind.Utc);
        }
    }
}
