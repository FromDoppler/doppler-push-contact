using Doppler.PushContact.Models.Entities;
using Doppler.PushContact.Models.Enums;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Doppler.PushContact.Transversal.Test
{
    public class WebPushEventsHelperTest
    {
        [Fact]
        public void MapGroupToMessageStats_ShouldKeepSentConsistent()
        {
            // Arrange
            var domain = "test-domain";
            var messageId = Guid.NewGuid();
            var now = DateTime.UtcNow;

            var events = new List<WebPushEvent>
            {
                new() { Domain = domain, MessageId = messageId, Date = now, Type = (int)WebPushEventType.Delivered },
                new() { Domain = domain, MessageId = messageId, Date = now, Type = (int)WebPushEventType.DeliveryFailed },
                new() { Domain = domain, MessageId = messageId, Date = now, Type = (int)WebPushEventType.ProcessingFailed },
                new() { Domain = domain, MessageId = messageId, Date = now, Type = (int)WebPushEventType.DeliveryFailedButRetry }
            };

            // Act
            var statsList = WebPushEventsHelper.MapWebPushEventsToMessageStatsGroups(events);

            // Assert
            Assert.Single(statsList);
            var stats = statsList.First();

            Assert.Equal(stats.Delivered + stats.NotDelivered, stats.Sent);
            Assert.Equal(4, stats.Sent);
            Assert.Equal(1, stats.Delivered);
            Assert.Equal(3, stats.NotDelivered);
        }

        [Fact]
        public void MapGroupToMessageStats_ShouldMapGroupsOK()
        {
            // Arrange
            var domainA = "test-domain-A";
            var domainB = "test-domain-B";

            var firstHour = new DateTime(2025, 10, 23, 7, 0, 0, DateTimeKind.Utc); // 2025-10-23T07:00:00
            var nextHour = firstHour.AddHours(1);

            var messageId1 = Guid.NewGuid();
            var messageId2 = Guid.NewGuid();

            var events = new List<WebPushEvent>
            {
                // --- Group 1: domain A, messageId1, first hour ---
                // shipping events
                new WebPushEvent { Domain = domainA, MessageId = messageId1, Date = firstHour, Type = (int)WebPushEventType.Delivered },
                new WebPushEvent { Domain = domainA, MessageId = messageId1, Date = firstHour.AddMinutes(10), Type = (int)WebPushEventType.DeliveryFailed },
                new WebPushEvent {
                    Domain = domainA,
                    MessageId = messageId1,
                    Date = firstHour.AddMinutes(15),
                    Type = (int)WebPushEventType.DeliveryFailed,
                    SubType = (int)WebPushEventSubType.InvalidSubcription,
                },
                new WebPushEvent { Domain = domainA, MessageId = messageId1, Date = firstHour.AddMinutes(20), Type = (int)WebPushEventType.DeliveryFailedButRetry },
                new WebPushEvent { Domain = domainA, MessageId = messageId1, Date = firstHour.AddMinutes(25), Type = (int)WebPushEventType.ProcessingFailed },
                // user interaction events
                new WebPushEvent { Domain = domainA, MessageId = messageId1, Date = firstHour.AddMinutes(30), Type = (int)WebPushEventType.Received },
                new WebPushEvent { Domain = domainA, MessageId = messageId1, Date = firstHour.AddMinutes(35), Type = (int)WebPushEventType.Clicked },
                new WebPushEvent { Domain = domainA, MessageId = messageId1, Date = firstHour.AddMinutes(40), Type = (int)WebPushEventType.ActionClick },

                // --- Group 2: domain A, messageId1, next hour ---
                new WebPushEvent { Domain = domainA, MessageId = messageId1, Date = nextHour.AddMinutes(5), Type = (int)WebPushEventType.Clicked },

                // --- Group 3: domain B, messageId2, first hour ---
                new WebPushEvent { Domain = domainB, MessageId = messageId2, Date = firstHour.AddMinutes(20), Type = (int)WebPushEventType.Delivered },
                new WebPushEvent { Domain = domainB, MessageId = messageId2, Date = firstHour.AddMinutes(40), Type = (int)WebPushEventType.DeliveryFailed },
                new WebPushEvent { Domain = domainB, MessageId = messageId2, Date = firstHour.AddMinutes(45), Type = (int)WebPushEventType.DeliveryFailedButRetry },
                new WebPushEvent { Domain = domainB, MessageId = messageId2, Date = firstHour.AddMinutes(50), Type = (int)WebPushEventType.ActionClick },
            };

            var truncatedFirstHour = new DateTime(firstHour.Year, firstHour.Month, firstHour.Day, firstHour.Hour, 0, 0, DateTimeKind.Utc);
            var truncatedNextHour = new DateTime(nextHour.Year, nextHour.Month, nextHour.Day, nextHour.Hour, 0, 0, DateTimeKind.Utc);

            // Act
            var statsList = WebPushEventsHelper.MapWebPushEventsToMessageStatsGroups(events);

            // Assert
            Assert.Equal(3, statsList.Count); // 3 groups

            // group1
            var group1 = statsList[0];
            Assert.Equal(group1.Delivered + group1.NotDelivered, group1.Sent);
            Assert.Equal(domainA, group1.Domain);
            Assert.Equal(messageId1, group1.MessageId);
            Assert.Equal(truncatedFirstHour, group1.Date);
            Assert.Equal(5, group1.Sent);
            Assert.Equal(1, group1.Delivered);
            Assert.Equal(4, group1.NotDelivered);
            Assert.Equal(2, group1.BillableSends);
            Assert.Equal(1, group1.Received);
            Assert.Equal(1, group1.Click);
            Assert.Equal(1, group1.ActionClick);

            // group2
            var group2 = statsList[1];
            Assert.Equal(group2.Delivered + group2.NotDelivered, group2.Sent);
            Assert.Equal(domainA, group2.Domain);
            Assert.Equal(messageId1, group2.MessageId);
            Assert.Equal(truncatedNextHour, group2.Date);
            Assert.Equal(0, group2.Sent);
            Assert.Equal(0, group2.Delivered);
            Assert.Equal(0, group2.NotDelivered);
            Assert.Equal(0, group2.BillableSends);
            Assert.Equal(0, group2.Received);
            Assert.Equal(1, group2.Click);
            Assert.Equal(0, group2.ActionClick);

            // group3
            var group3 = statsList[2];
            Assert.Equal(group3.Delivered + group3.NotDelivered, group3.Sent);
            Assert.Equal(domainB, group3.Domain);
            Assert.Equal(messageId2, group3.MessageId);
            Assert.Equal(truncatedFirstHour, group3.Date);
            Assert.Equal(3, group3.Sent);
            Assert.Equal(1, group3.Delivered);
            Assert.Equal(2, group3.NotDelivered);
            Assert.Equal(1, group3.BillableSends);
            Assert.Equal(0, group3.Received);
            Assert.Equal(0, group3.Click);
            Assert.Equal(1, group3.ActionClick);
        }
    }
}
