using Doppler.PushContact.Models.Entities;
using Doppler.PushContact.Models.Enums;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Doppler.PushContact.Transversal
{
    public static class WebPushEventsMapper
    {
        public static List<MessageStats> MapWebPushEventsToMessageStatsGroups(IEnumerable<WebPushEvent> webPushEvents)
        {
            if (webPushEvents == null || !webPushEvents.Any())
            {
                return null;
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

            return groupedStats;
        }

        private static DateTime TruncateToHour(DateTime date)
        {
            return new DateTime(date.Year, date.Month, date.Day, date.Hour, 0, 0, DateTimeKind.Utc);
        }
    }
}
