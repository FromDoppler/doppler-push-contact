using Doppler.PushContact.Models.Entities;
using Doppler.PushContact.Models.Enums;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Doppler.PushContact.Transversal
{
    public static class WebPushEventsHelper
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
            var deliveredCount = GetDeliveredCount(events);
            var notDeliveredCount = GetNotDeliveredCount(events);

            return new MessageStats
            {
                Domain = domain,
                MessageId = messageId,
                Date = date,

                // ensure: Sent = Delivered + NotDelivered
                Sent = deliveredCount + notDeliveredCount,
                Delivered = deliveredCount,
                NotDelivered = notDeliveredCount,
                BillableSends = GetBillableSendsCount(events),

                Received = events.Count(x => x.Type == (int)WebPushEventType.Received),
                Click = events.Count(x => x.Type == (int)WebPushEventType.Clicked),
                ActionClick = events.Count(x => x.Type == (int)WebPushEventType.ActionClick),
            };
        }

        public static int GetDeliveredCount(IEnumerable<WebPushEvent> events) =>
            events?.Count(x => x.Type == (int)WebPushEventType.Delivered) ?? 0;

        public static int GetNotDeliveredCount(IEnumerable<WebPushEvent> events) =>
            events?.Count(x =>
                x.Type == (int)WebPushEventType.DeliveryFailed ||
                x.Type == (int)WebPushEventType.ProcessingFailed ||
                x.Type == (int)WebPushEventType.DeliveryFailedButRetry
            ) ?? 0;

        public static int GetBillableSendsCount(IEnumerable<WebPushEvent> events) =>
            events?.Count(x =>
                x.Type == (int)WebPushEventType.Delivered ||
                (x.Type == (int)WebPushEventType.DeliveryFailed &&
                x.SubType == (int)WebPushEventSubType.InvalidSubcription)
            ) ?? 0;

        private static DateTime TruncateToHour(DateTime date)
        {
            return new DateTime(date.Year, date.Month, date.Day, date.Hour, 0, 0, DateTimeKind.Utc);
        }
    }
}
