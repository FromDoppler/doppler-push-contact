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

            return webPushEvents
                .GroupBy(e => new
                {
                    e.Domain,
                    e.MessageId,
                    Date = TruncateToHour(e.Date)
                })
                .Select(g => MapGroupToMessageStats(g.Key.Domain, g.Key.MessageId, g.Key.Date, g))
                .ToList();
        }

        public static MessageStats MapSingleWebPushEventToMessageStats(WebPushEvent webPushEvent)
        {
            if (webPushEvent == null)
            {
                return null;
            }

            return MapGroupToMessageStats(
                webPushEvent.Domain,
                webPushEvent.MessageId,
                TruncateToHour(webPushEvent.Date),
                [webPushEvent]
            );
        }

        private static MessageStats MapGroupToMessageStats(
            string domain,
            Guid messageId,
            DateTime date,
            IEnumerable<WebPushEvent> events
        )
        {
            return new MessageStats
            {
                Domain = domain,
                MessageId = messageId,
                Date = date,

                Sent = events.Count(x => ShouldCountAsSent(x.Type)),
                Delivered = events.Count(x => x.Type == (int)WebPushEventType.Delivered),
                Received = events.Count(x => x.Type == (int)WebPushEventType.Received),
                Click = events.Count(x => x.Type == (int)WebPushEventType.Clicked),
                ActionClick = events.Count(x => x.Type == (int)WebPushEventType.ActionClick),

                NotDelivered = events.Count(x =>
                    x.Type == (int)WebPushEventType.DeliveryFailed ||
                    x.Type == (int)WebPushEventType.ProcessingFailed),

                BillableSends = events.Count(x =>
                    x.Type == (int)WebPushEventType.Delivered ||
                    (x.Type == (int)WebPushEventType.DeliveryFailed &&
                    x.SubType == (int)WebPushEventSubType.InvalidSubcription))
            };
        }

        public static bool ShouldCountAsSent(int webPushEventType)
        {
            return webPushEventType == (int)WebPushEventType.Delivered ||
                webPushEventType == (int)WebPushEventType.DeliveryFailed ||

                // TODO: at moment these are counted as sent, but they should be retried
                webPushEventType == (int)WebPushEventType.DeliveryFailedButRetry ||
                webPushEventType == (int)WebPushEventType.ProcessingFailed;
        }

        private static DateTime TruncateToHour(DateTime date)
        {
            return new DateTime(date.Year, date.Month, date.Day, date.Hour, 0, 0, DateTimeKind.Utc);
        }
    }
}
