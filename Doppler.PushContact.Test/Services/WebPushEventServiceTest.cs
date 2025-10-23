using AutoFixture;
using Doppler.PushContact.DTOs;
using Doppler.PushContact.Models.Entities;
using Doppler.PushContact.Models.Enums;
using Doppler.PushContact.Repositories.Interfaces;
using Doppler.PushContact.Services;
using Doppler.PushContact.Services.Messages;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Doppler.PushContact.Test.Services
{
    public class WebPushEventServiceTest
    {
        private static WebPushEventService CreateSut(
            IWebPushEventRepository webPushEventRepository = null,
            IPushContactService pushContactService = null,
            IMessageRepository messageRepository = null,
            IMessageStatsRepository messageStatsRepository = null,
            ILogger<WebPushEventService> logger = null)
        {
            return new WebPushEventService(
                webPushEventRepository ?? Mock.Of<IWebPushEventRepository>(),
                pushContactService ?? Mock.Of<IPushContactService>(),
                messageRepository ?? Mock.Of<IMessageRepository>(),
                messageStatsRepository ?? Mock.Of<IMessageStatsRepository>(),
                logger ?? Mock.Of<ILogger<WebPushEventService>>()
            );
        }

        [Fact]
        public async Task GetWebPushEventSummarizationAsync_should_log_error_and_return_empty_stats_when_repository_throw_exception()
        {
            // Arrange
            var fixture = new Fixture();

            var messageId = fixture.Create<Guid>();
            var expectedMessageException = $"Error summarizing 'WebPushEvents' with {nameof(messageId)} {messageId}";

            var mockRepository = new Mock<IWebPushEventRepository>();
            var mockLogger = new Mock<ILogger<WebPushEventService>>();

            mockRepository
                .Setup(repo => repo.GetWebPushEventSummarization(messageId))
                .ThrowsAsync(new Exception("Repository exception"));

            var sut = CreateSut(webPushEventRepository: mockRepository.Object, logger: mockLogger.Object);

            // Act
            var result = await sut.GetWebPushEventSummarizationAsync(messageId);

            mockLogger.Verify(
                x => x.Log(
                    It.Is<LogLevel>(l => l == LogLevel.Error),
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains(expectedMessageException)),
                    It.IsAny<Exception>(),
                    (Func<It.IsAnyType, Exception, string>)It.IsAny<object>()
                ),
                Times.Once
            );

            Assert.NotNull(result);
            Assert.Equal(messageId, result.MessageId);
            Assert.Equal(0, result.SentQuantity);
            Assert.Equal(0, result.Delivered);
            Assert.Equal(0, result.NotDelivered);
        }

        [Fact]
        public async Task GetWebPushEventSummarizationAsync_should_return_stats_ok()
        {
            // Arrange
            var fixture = new Fixture();

            var messageId = fixture.Create<Guid>();

            var mockRepository = new Mock<IWebPushEventRepository>();
            var mockLogger = new Mock<ILogger<WebPushEventService>>();

            var expectedSummarization = new WebPushEventSummarizationDTO
            {
                MessageId = messageId,
                SentQuantity = 10,
                Delivered = 8,
                NotDelivered = 2
            };

            mockRepository
                .Setup(repo => repo.GetWebPushEventSummarization(messageId))
                .ReturnsAsync(expectedSummarization);

            var sut = CreateSut(webPushEventRepository: mockRepository.Object, logger: mockLogger.Object);

            // Act
            var result = await sut.GetWebPushEventSummarizationAsync(messageId);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(expectedSummarization.MessageId, result.MessageId);
            Assert.Equal(expectedSummarization.SentQuantity, result.SentQuantity);
            Assert.Equal(expectedSummarization.Delivered, result.Delivered);
            Assert.Equal(expectedSummarization.NotDelivered, result.NotDelivered);
        }

        [Fact]
        public async Task RegisterWebPushEventAsync_ShouldReturnFalse_WhenDomainsAreDifferent()
        {
            // Arrange
            var fixture = new Fixture();
            var contactId = fixture.Create<string>();
            var messageId = fixture.Create<Guid>();
            var eventType = WebPushEventType.Delivered;

            var mockPushContactService = new Mock<IPushContactService>();
            var mockMessageRepository = new Mock<IMessageRepository>();
            var mockWebPushEventRepository = new Mock<IWebPushEventRepository>();
            var mockLogger = new Mock<ILogger<WebPushEventService>>();

            mockPushContactService
                .Setup(service => service.GetPushContactDomainAsync(contactId))
                .ReturnsAsync("domain1");

            mockMessageRepository
                .Setup(repo => repo.GetMessageDomainAsync(messageId))
                .ReturnsAsync("domain2");

            var sut = CreateSut(
                webPushEventRepository: mockWebPushEventRepository.Object,
                pushContactService: mockPushContactService.Object,
                messageRepository: mockMessageRepository.Object,
                logger: mockLogger.Object
            );

            // Act
            var result = await sut.RegisterWebPushEventAsync(contactId, messageId, eventType, null, CancellationToken.None);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public async Task RegisterWebPushEventAsync_ShouldReturnFalse_WhenEventIsAlreadyRegistered()
        {
            // Arrange
            var fixture = new Fixture();
            var contactId = fixture.Create<string>();
            var messageId = fixture.Create<Guid>();
            var eventType = WebPushEventType.Delivered;

            var mockPushContactService = new Mock<IPushContactService>();
            var mockMessageRepository = new Mock<IMessageRepository>();
            var mockWebPushEventRepository = new Mock<IWebPushEventRepository>();
            var mockLogger = new Mock<ILogger<WebPushEventService>>();

            mockPushContactService
                .Setup(service => service.GetPushContactDomainAsync(contactId))
                .ReturnsAsync("domain");

            mockMessageRepository
                .Setup(repo => repo.GetMessageDomainAsync(messageId))
                .ReturnsAsync("domain");

            mockWebPushEventRepository
                .Setup(repo => repo.IsWebPushEventRegistered(contactId, messageId, eventType))
                .ReturnsAsync(true);

            var sut = CreateSut(
                webPushEventRepository: mockWebPushEventRepository.Object,
                pushContactService: mockPushContactService.Object,
                messageRepository: mockMessageRepository.Object,
                logger: mockLogger.Object
            );

            // Act
            var result = await sut.RegisterWebPushEventAsync(contactId, messageId, eventType, null, CancellationToken.None);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public async Task RegisterWebPushEventAsync_ShouldReturnFalse_WhenInsertionFails()
        {
            // Arrange
            var fixture = new Fixture();
            var contactId = fixture.Create<string>();
            var messageId = fixture.Create<Guid>();
            var eventType = WebPushEventType.Delivered;

            var mockPushContactService = new Mock<IPushContactService>();
            var mockMessageRepository = new Mock<IMessageRepository>();
            var mockWebPushEventRepository = new Mock<IWebPushEventRepository>();
            var mockLogger = new Mock<ILogger<WebPushEventService>>();

            mockPushContactService
                .Setup(service => service.GetPushContactDomainAsync(contactId))
                .ReturnsAsync("domain");

            mockMessageRepository
                .Setup(repo => repo.GetMessageDomainAsync(messageId))
                .ReturnsAsync("domain");

            mockWebPushEventRepository
                .Setup(repo => repo.IsWebPushEventRegistered(contactId, messageId, eventType))
                .ReturnsAsync(false);

            mockWebPushEventRepository
                .Setup(repo => repo.InsertAsync(It.IsAny<WebPushEvent>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new Exception("Insertion exception"));

            var sut = CreateSut(
                webPushEventRepository: mockWebPushEventRepository.Object,
                pushContactService: mockPushContactService.Object,
                messageRepository: mockMessageRepository.Object,
                logger: mockLogger.Object
            );

            // Act
            var result = await sut.RegisterWebPushEventAsync(contactId, messageId, eventType, null, CancellationToken.None);

            // Assert
            Assert.False(result);

            mockLogger.Verify(
                x => x.Log(
                    It.Is<LogLevel>(l => l == LogLevel.Error),
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains($"Unexpected error while registering WebPushEvent for contactId: {contactId}, messageId: {messageId}, eventType: {eventType}")),
                    It.IsAny<Exception>(),
                    (Func<It.IsAnyType, Exception, string>)It.IsAny<object>()
                ),
                Times.Once
            );
        }

        [Fact]
        public async Task RegisterWebPushEventAsync_ShouldReturnTrue_WhenEventIsSuccessfullyRegistered()
        {
            // Arrange
            var fixture = new Fixture();
            var contactId = fixture.Create<string>();
            var messageId = fixture.Create<Guid>();
            var eventType = WebPushEventType.Delivered;

            var mockPushContactService = new Mock<IPushContactService>();
            var mockMessageRepository = new Mock<IMessageRepository>();
            var mockWebPushEventRepository = new Mock<IWebPushEventRepository>();
            var mockLogger = new Mock<ILogger<WebPushEventService>>();

            mockPushContactService
                .Setup(service => service.GetPushContactDomainAsync(contactId))
                .ReturnsAsync("domain");

            mockMessageRepository
                .Setup(repo => repo.GetMessageDomainAsync(messageId))
                .ReturnsAsync("domain");

            mockWebPushEventRepository
                .Setup(repo => repo.IsWebPushEventRegistered(contactId, messageId, eventType))
                .ReturnsAsync(false);

            mockWebPushEventRepository
                .Setup(repo => repo.InsertAsync(It.IsAny<WebPushEvent>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            var sut = CreateSut(
                webPushEventRepository: mockWebPushEventRepository.Object,
                pushContactService: mockPushContactService.Object,
                messageRepository: mockMessageRepository.Object,
                logger: mockLogger.Object
            );

            // Act
            var result = await sut.RegisterWebPushEventAsync(contactId, messageId, eventType, null, CancellationToken.None);

            // Assert
            Assert.True(result);
        }

        [Fact]
        public async Task RegisterWebPushEventAsync_ShouldReturnTrueAndSaveEventDescriptor_WhenEventIsSuccessfullyRegistered()
        {
            // Arrange
            var fixture = new Fixture();
            var contactId = fixture.Create<string>();
            var messageId = fixture.Create<Guid>();
            var eventType = WebPushEventType.ActionClick;

            var mockPushContactService = new Mock<IPushContactService>();
            var mockMessageRepository = new Mock<IMessageRepository>();
            var mockWebPushEventRepository = new Mock<IWebPushEventRepository>();
            var mockLogger = new Mock<ILogger<WebPushEventService>>();

            mockPushContactService
                .Setup(service => service.GetPushContactDomainAsync(contactId))
                .ReturnsAsync("domain");

            mockMessageRepository
                .Setup(repo => repo.GetMessageDomainAsync(messageId))
                .ReturnsAsync("domain");

            mockWebPushEventRepository
                .Setup(repo => repo.IsWebPushEventRegistered(contactId, messageId, eventType))
                .ReturnsAsync(false);

            mockWebPushEventRepository
                .Setup(repo => repo.InsertAsync(It.IsAny<WebPushEvent>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            var sut = CreateSut(
                webPushEventRepository: mockWebPushEventRepository.Object,
                pushContactService: mockPushContactService.Object,
                messageRepository: mockMessageRepository.Object,
                logger: mockLogger.Object
            );

            // Act
            var result = await sut.RegisterWebPushEventAsync(contactId, messageId, eventType, "action_test", CancellationToken.None);

            // Assert
            Assert.True(result);
        }

        [Fact]
        public async Task GetWebPushEventConsumed_ShouldReturnConsumed_Ok()
        {
            // Arrange
            var domain = "test.com";
            var from = DateTimeOffset.UtcNow.AddDays(-1);
            var to = DateTimeOffset.UtcNow;

            var webPushEventsConsumed = 10;

            var mockPushContactService = new Mock<IPushContactService>();
            var mockWebPushEventRepository = new Mock<IWebPushEventRepository>();

            mockWebPushEventRepository
                .Setup(repo => repo.GetWebPushEventConsumed(domain, from, to))
                .ReturnsAsync(webPushEventsConsumed);

            var sut = CreateSut(
                webPushEventRepository: mockWebPushEventRepository.Object,
                pushContactService: mockPushContactService.Object
            );

            // Act
            var consumedResult = await sut.GetWebPushEventConsumed(domain, from, to);

            // Assert
            Assert.Equal(webPushEventsConsumed, consumedResult);
        }

        public static IEnumerable<object[]> GetEmptyWebPushEventsList()
        {
            yield return new object[] { null };
            yield return new object[] { new List<WebPushEvent>() };
        }

        [Theory]
        [MemberData(nameof(GetEmptyWebPushEventsList))]
        public async Task RegisterWebPushEventsAsync_ShouldNotCallToRepository_WhenWebPushEventsListIsNullOrEmpty(IEnumerable<WebPushEvent> webPushEvents)
        {
            // Arrange
            Fixture fixture = new Fixture();
            var domain = fixture.Create<string>();
            var messageId = fixture.Create<Guid>();

            var mockRepository = new Mock<IWebPushEventRepository>();

            var sut = CreateSut(webPushEventRepository: mockRepository.Object);

            // Act
            await sut.RegisterWebPushEventsAsync(messageId, webPushEvents, false);

            // Assert
            mockRepository.Verify(x => x.BulkInsertAsync(It.IsAny<IEnumerable<WebPushEvent>>()), Times.Never);
        }

        [Fact]
        public async Task RegisterWebPushEventsAsync_ShouldCallToRepository_WhenWebPushEventsListHasElements()
        {
            // Arrange
            Fixture fixture = new Fixture();
            var domain = fixture.Create<string>();
            var messageId = fixture.Create<Guid>();

            var webPushEvents = new List<WebPushEvent>()
            {
                new WebPushEvent()
                {
                    Date = DateTime.UtcNow,
                    Domain = domain,
                    MessageId = messageId,
                    Type = (int)WebPushEventType.Delivered,
                    SubType = (int)WebPushEventSubType.None,
                },
            };

            var mockWebPushEventRepository = new Mock<IWebPushEventRepository>();

            var sut = CreateSut(
                webPushEventRepository: mockWebPushEventRepository.Object
            );

            // Act
            await sut.RegisterWebPushEventsAsync(messageId, webPushEvents, false);

            // Assert
            mockWebPushEventRepository.Verify(x => x.BulkInsertAsync(webPushEvents), Times.Once);
        }

        [Fact]
        public async Task RegisterWebPushEventsAsync_ShouldCallToRepository_OnlyWithFailedEvents_WhenIndicateRegisterOnlyFailed()
        {
            // Arrange
            Fixture fixture = new Fixture();
            var domain = fixture.Create<string>();
            var messageId = fixture.Create<Guid>();

            var webPushEvents = new List<WebPushEvent>()
            {
                new WebPushEvent()
                {
                    Date = DateTime.UtcNow,
                    Domain = domain,
                    MessageId = messageId,
                    Type = (int)WebPushEventType.Delivered,
                    SubType = (int)WebPushEventSubType.None,
                },
                new WebPushEvent()
                {
                    Date = DateTime.UtcNow,
                    Domain = domain,
                    MessageId = messageId,
                    Type = (int)WebPushEventType.DeliveryFailed,
                    SubType = (int)WebPushEventSubType.None,
                },
                new WebPushEvent()
                {
                    Date = DateTime.UtcNow,
                    Domain = domain,
                    MessageId = messageId,
                    Type = (int)WebPushEventType.DeliveryFailed,
                    SubType = (int)WebPushEventSubType.InvalidSubcription,
                },
            };

            var failedEvents = webPushEvents
                .Where(x => x.Type == (int)WebPushEventType.DeliveryFailed)
                .ToList();

            var registerOnlyFailed = true;

            var mockWebPushEventRepository = new Mock<IWebPushEventRepository>();

            var sut = CreateSut(
                webPushEventRepository: mockWebPushEventRepository.Object
            );

            // Act
            await sut.RegisterWebPushEventsAsync(messageId, webPushEvents, registerOnlyFailed);

            // Assert
            mockWebPushEventRepository.Verify(x => x.BulkInsertAsync(failedEvents), Times.Once);
        }

        [Fact]
        public async Task RegisterWebPushEventsAsync_ShouldNotCallToRepository_WhenIndicateRegisterOnlyFailed_ButFailedEventsListIsEmpty()
        {
            // Arrange
            Fixture fixture = new Fixture();
            var domain = fixture.Create<string>();
            var messageId = fixture.Create<Guid>();

            var webPushEvents = new List<WebPushEvent>()
            {
                new WebPushEvent()
                {
                    Date = DateTime.UtcNow,
                    Domain = domain,
                    MessageId = messageId,
                    Type = (int)WebPushEventType.Delivered,
                    SubType = (int)WebPushEventSubType.None,
                },
            };

            var registerOnlyFailed = true;

            var mockWebPushEventRepository = new Mock<IWebPushEventRepository>();

            var sut = CreateSut(
                webPushEventRepository: mockWebPushEventRepository.Object
            );

            // Act
            await sut.RegisterWebPushEventsAsync(messageId, webPushEvents, registerOnlyFailed);

            // Assert
            mockWebPushEventRepository.Verify(x => x.BulkInsertAsync(It.IsAny<IEnumerable<WebPushEvent>>()), Times.Never);
        }


        [Fact]
        public async Task RegisterWebPushEventsAsync_ShouldLogError_WhenRepositoryThrowAnException()
        {
            // Arrange
            Fixture fixture = new Fixture();
            var domain = fixture.Create<string>();
            var messageId = fixture.Create<Guid>();
            var exceptionMessage = "Testing exception";

            var webPushEvents = new List<WebPushEvent>()
            {
                new WebPushEvent()
                {
                    Date = DateTime.UtcNow,
                    Domain = domain,
                    MessageId = messageId,
                    Type = (int)WebPushEventType.Delivered,
                    SubType = (int)WebPushEventSubType.None,
                },
            };

            var mockWebPushEventRepository = new Mock<IWebPushEventRepository>();
            mockWebPushEventRepository
                .Setup(mwpr => mwpr.BulkInsertAsync(webPushEvents))
                .Throws(new Exception(exceptionMessage));

            var mockLogger = new Mock<ILogger<WebPushEventService>>();

            var sut = CreateSut(
                webPushEventRepository: mockWebPushEventRepository.Object,
                logger: mockLogger.Object
            );

            // Act
            await sut.RegisterWebPushEventsAsync(messageId, webPushEvents, false);

            // Assert
            mockLogger.Verify(
                x => x.Log(
                    It.Is<LogLevel>(l => l == LogLevel.Error),
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Unexpected error while registering WebPushEvents for messageId:")),
                    It.Is<Exception>(e => e.Message == exceptionMessage),
                    (Func<It.IsAnyType, Exception, string>)It.IsAny<object>()
                ),
                Times.Once
            );
        }
    }
}
