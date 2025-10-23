using AutoFixture;
using Doppler.PushContact.Models.Entities;
using Doppler.PushContact.Models.Enums;
using Doppler.PushContact.Repositories.Interfaces;
using Doppler.PushContact.Services;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Doppler.PushContact.Test.Services
{
    public class MessageStatsServiceTest
    {
        private static MessageStatsService CreateSut(
            IMessageStatsRepository repository = null,
            ILogger<MessageStatsService> logger = null
        )
        {
            return new MessageStatsService(
                repository ?? Mock.Of<IMessageStatsRepository>(),
                logger ?? Mock.Of<ILogger<MessageStatsService>>()
            );
        }

        [Fact]
        public async Task RegisterMessageStatsAsync_should_not_call_repository_when_webPushEvents_is_null_or_empty()
        {
            // Arrange
            var mockRepository = new Mock<IMessageStatsRepository>();
            var sut = CreateSut(repository: mockRepository.Object);

            // Act
            await sut.RegisterMessageStatsAsync(null); // null case
            await sut.RegisterMessageStatsAsync(new List<WebPushEvent>()); // empty case

            // Assert
            mockRepository.Verify(r => r.BulkUpsertStatsAsync(It.IsAny<IEnumerable<MessageStats>>()), Times.Never);
        }

        [Fact]
        public async Task RegisterMessageStatsAsync_should_log_error_when_repository_throws_exception()
        {
            // Arrange
            var mockRepository = new Mock<IMessageStatsRepository>();
            var mockLogger = new Mock<ILogger<MessageStatsService>>();

            mockRepository
                .Setup(r => r.BulkUpsertStatsAsync(It.IsAny<IEnumerable<MessageStats>>()))
                .ThrowsAsync(new Exception("DB error"));

            var webPushEvents = new List<WebPushEvent>
            {
                new WebPushEvent
                {
                    Domain = "example.com",
                    MessageId = Guid.NewGuid(),
                    Date = DateTime.UtcNow,
                    Type = (int)WebPushEventType.Delivered
                }
            };

            var sut = CreateSut(repository: mockRepository.Object, logger: mockLogger.Object);

            // Act
            await sut.RegisterMessageStatsAsync(webPushEvents);

            // Assert
            mockLogger.Verify(
                x => x.Log(
                    It.Is<LogLevel>(l => l == LogLevel.Error),
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Error while registering MessageStats")),
                    It.IsAny<Exception>(),
                    (Func<It.IsAnyType, Exception, string>)It.IsAny<object>()
                ),
                Times.Once
            );
        }

        [Fact]
        public async Task RegisterMessageStatsAsync_should_group_and_count_events_correctly()
        {
            // Arrange
            Fixture fixture = new Fixture();

            var messageId = fixture.Create<Guid>();
            var domain = fixture.Create<string>();
            var date = new DateTime(2025, 10, 23, 7, 5, 0, DateTimeKind.Utc); // 2025-10-23T07:05:00
            var hourTruncated = new DateTime(date.Year, date.Month, date.Day, date.Hour, 0, 0, DateTimeKind.Utc);

            var events = new List<WebPushEvent>
            {
                new WebPushEvent { Domain = domain, MessageId = messageId, Date = date, Type = (int)WebPushEventType.Delivered },
                new WebPushEvent { Domain = domain, MessageId = messageId, Date = date.AddMinutes(5), Type = (int)WebPushEventType.Received },
                new WebPushEvent { Domain = domain, MessageId = messageId, Date = date.AddMinutes(10), Type = (int)WebPushEventType.Clicked },
                new WebPushEvent {
                    Domain = domain,
                    MessageId = messageId,
                    Date = date.AddMinutes(15),
                    Type = (int)WebPushEventType.DeliveryFailed,
                    SubType = (int)WebPushEventSubType.InvalidSubcription
                },
            };

            var mockRepository = new Mock<IMessageStatsRepository>();
            var sut = CreateSut(repository: mockRepository.Object);

            // Act
            await sut.RegisterMessageStatsAsync(events);

            // Assert
            mockRepository.Verify(r =>
                r.BulkUpsertStatsAsync(It.Is<IEnumerable<MessageStats>>(list =>
                    list.Count() == 1 &&
                    list.First().Domain == domain &&
                    list.First().MessageId == messageId &&
                    list.First().Date == hourTruncated &&
                    list.First().Sent == 4 &&
                    list.First().Delivered == 1 &&
                    list.First().NotDelivered == 1 &&
                    list.First().BillableSends == 2 &&
                    list.First().Received == 1 &&
                    list.First().Click == 1 &&
                    list.First().ActionClick == 0
                )),
                Times.Once
            );
        }

        [Fact]
        public async Task RegisterMessageStatsAsync_should_generate_several_groups_by_domain_messageId_and_hour_correctly()
        {
            // Arrange
            var firstHour = new DateTime(2025, 10, 23, 7, 0, 0, DateTimeKind.Utc); // 2025-10-23T07:00:00
            var nextHour = firstHour.AddHours(1);

            var messageId1 = Guid.NewGuid();
            var messageId2 = Guid.NewGuid();

            var events = new List<WebPushEvent>
            {
                // --- Group 1: domain A, messageId1, first hour ---
                new WebPushEvent { Domain = "a.com", MessageId = messageId1, Date = firstHour, Type = (int)WebPushEventType.Delivered },
                new WebPushEvent { Domain = "a.com", MessageId = messageId1, Date = firstHour.AddMinutes(10), Type = (int)WebPushEventType.Received },

                // --- Group 2: domain A, messageId1, next hour ---
                new WebPushEvent { Domain = "a.com", MessageId = messageId1, Date = nextHour.AddMinutes(5), Type = (int)WebPushEventType.Clicked },

                // --- Group 3: domain B, messageId2, first hour ---
                new WebPushEvent { Domain = "b.com", MessageId = messageId2, Date = firstHour.AddMinutes(20), Type = (int)WebPushEventType.Delivered },
                new WebPushEvent { Domain = "b.com", MessageId = messageId2, Date = firstHour.AddMinutes(40), Type = (int)WebPushEventType.DeliveryFailed }
            };

            var truncatedFirstHour = new DateTime(firstHour.Year, firstHour.Month, firstHour.Day, firstHour.Hour, 0, 0, DateTimeKind.Utc);
            var truncatedNextHour = new DateTime(nextHour.Year, nextHour.Month, nextHour.Day, nextHour.Hour, 0, 0, DateTimeKind.Utc);

            var mockRepository = new Mock<IMessageStatsRepository>();
            var sut = CreateSut(repository: mockRepository.Object);

            // Act
            await sut.RegisterMessageStatsAsync(events);

            // Assert
            mockRepository.Verify(r =>
                r.BulkUpsertStatsAsync(It.Is<IEnumerable<MessageStats>>(list =>
                    list.Count() == 3 &&

                    // Group 1: domain A, first hour
                    list.Any(x =>
                        x.Domain == "a.com" &&
                        x.MessageId == messageId1 &&
                        x.Date == truncatedFirstHour &&
                        x.Sent == 2 &&
                        x.Delivered == 1 &&
                        x.Received == 1 &&
                        x.Click == 0 &&
                        x.NotDelivered == 0 &&
                        x.BillableSends == 1
                    ) &&

                    // Group 2: domain A, next hour
                    list.Any(x =>
                        x.Domain == "a.com" &&
                        x.MessageId == messageId1 &&
                        x.Date == truncatedNextHour &&
                        x.Sent == 1 &&
                        x.Delivered == 0 &&
                        x.Received == 0 &&
                        x.Click == 1 &&
                        x.NotDelivered == 0 &&
                        x.BillableSends == 0
                    ) &&

                    // Group 3: domain B, first hour
                    list.Any(x =>
                        x.Domain == "b.com" &&
                        x.MessageId == messageId2 &&
                        x.Date == truncatedFirstHour &&
                        x.Sent == 2 &&
                        x.Delivered == 1 &&
                        x.Received == 0 &&
                        x.Click == 0 &&
                        x.NotDelivered == 1 &&
                        x.BillableSends == 1
                    )
                )),
                Times.Once
            );
        }
    }
}
