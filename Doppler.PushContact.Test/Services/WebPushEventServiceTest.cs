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
            ILogger<WebPushEventService> logger = null)
        {
            return new WebPushEventService(
                webPushEventRepository ?? Mock.Of<IWebPushEventRepository>(),
                pushContactService ?? Mock.Of<IPushContactService>(),
                messageRepository ?? Mock.Of<IMessageRepository>(),
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

        public static IEnumerable<object[]> GetInvalidSendMessageResults()
        {
            yield return new object[] { null };
            yield return new object[] { new SendMessageResult { SendMessageTargetResult = null } };
            yield return new object[] { new SendMessageResult { SendMessageTargetResult = new List<SendMessageTargetResult>() } };
        }

        [Theory]
        [MemberData(nameof(GetInvalidSendMessageResults))]
        public async Task RegisterWebPushEventsAsync_ShouldReturnNull_WhenSendMessageResultsIsNullOrEmpty(SendMessageResult sendMessageResult)
        {
            // Arrange
            Fixture fixture = new Fixture();
            var domain = fixture.Create<string>();
            var messageId = fixture.Create<Guid>();

            var sut = CreateSut();

            // Act
            var result = await sut.RegisterWebPushEventsAsync(domain, messageId, sendMessageResult);

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public async Task RegisterWebPushEventsAsync_ShouldReturnWebPushEvents_WhenSendMessageResultsHaveElements()
        {
            // Arrange
            Fixture fixture = new Fixture();
            var domain = fixture.Create<string>();
            var messageId = fixture.Create<Guid>();
            var deviceTokenValid = fixture.Create<string>();
            var deviceTokenInvalid = fixture.Create<string>();
            var deviceTokenWithUnknownFailure = fixture.Create<string>();
            var errorMessageInvalidToken = "invalid token";
            var errorMessageUnknownFailure = "unknown failure";

            var sendMessageResult = new SendMessageResult()
            {
                SendMessageTargetResult = new List<SendMessageTargetResult>()
                {
                    new SendMessageTargetResult()
                    {
                        IsSuccess = true,
                        IsValidTargetDeviceToken = true,
                        TargetDeviceToken = deviceTokenValid,
                        NotSuccessErrorDetails = string.Empty,
                    },
                    new SendMessageTargetResult()
                    {
                        IsSuccess = false,
                        IsValidTargetDeviceToken = false,
                        TargetDeviceToken = deviceTokenInvalid,
                        NotSuccessErrorDetails = errorMessageInvalidToken,
                    },
                    new SendMessageTargetResult()
                    {
                        IsSuccess = false,
                        IsValidTargetDeviceToken = true,
                        TargetDeviceToken = deviceTokenWithUnknownFailure,
                        NotSuccessErrorDetails = errorMessageUnknownFailure,
                    }
                }
            };

            var mockWebPushEventRepository = new Mock<IWebPushEventRepository>();
            mockWebPushEventRepository.Setup(mwpr => mwpr.BulkInsertAsync(It.IsAny<IEnumerable<WebPushEvent>>()));

            var sut = CreateSut(
                webPushEventRepository: mockWebPushEventRepository.Object
            );

            // Act
            var result = (await sut.RegisterWebPushEventsAsync(domain, messageId, sendMessageResult)).ToList();

            // Assert
            Assert.NotNull(result);
            Assert.Equal(sendMessageResult.SendMessageTargetResult.Count(), result.Count());

            // webPushEvent with DeviceToken valid
            var webPushEventResult_DeviceTokenValid = result.FirstOrDefault(r => r.DeviceToken == deviceTokenValid);
            Assert.Equal(domain, webPushEventResult_DeviceTokenValid.Domain);
            Assert.Equal(messageId, webPushEventResult_DeviceTokenValid.MessageId);
            Assert.Equal(deviceTokenValid, webPushEventResult_DeviceTokenValid.DeviceToken);
            Assert.Null(webPushEventResult_DeviceTokenValid.PushContactId);
            Assert.Equal((int)WebPushEventType.Delivered, webPushEventResult_DeviceTokenValid.Type);
            Assert.Equal((int)WebPushEventSubType.None, webPushEventResult_DeviceTokenValid.SubType);
            Assert.Equal(string.Empty, webPushEventResult_DeviceTokenValid.ErrorMessage);

            // webPushEvent with DeviceToken invalid
            var webPushEventResult_DeviceTokenInvalid = result.FirstOrDefault(r => r.DeviceToken == deviceTokenInvalid);
            Assert.Equal(domain, webPushEventResult_DeviceTokenInvalid.Domain);
            Assert.Equal(messageId, webPushEventResult_DeviceTokenInvalid.MessageId);
            Assert.Equal(deviceTokenInvalid, webPushEventResult_DeviceTokenInvalid.DeviceToken);
            Assert.Null(webPushEventResult_DeviceTokenValid.PushContactId);
            Assert.Equal((int)WebPushEventType.DeliveryFailed, webPushEventResult_DeviceTokenInvalid.Type);
            Assert.Equal((int)WebPushEventSubType.InvalidSubcription, webPushEventResult_DeviceTokenInvalid.SubType);
            Assert.Equal(errorMessageInvalidToken, webPushEventResult_DeviceTokenInvalid.ErrorMessage);

            // webPushEvent with DeviceToken with unknown failure
            var webPushEventResult_DeviceTokenUnknownFailure = result.FirstOrDefault(r => r.DeviceToken == deviceTokenWithUnknownFailure);
            Assert.Equal(domain, webPushEventResult_DeviceTokenUnknownFailure.Domain);
            Assert.Equal(messageId, webPushEventResult_DeviceTokenUnknownFailure.MessageId);
            Assert.Equal(deviceTokenWithUnknownFailure, webPushEventResult_DeviceTokenUnknownFailure.DeviceToken);
            Assert.Null(webPushEventResult_DeviceTokenUnknownFailure.PushContactId);
            Assert.Equal((int)WebPushEventType.DeliveryFailed, webPushEventResult_DeviceTokenUnknownFailure.Type);
            Assert.Equal((int)WebPushEventSubType.UnknownFailure, webPushEventResult_DeviceTokenUnknownFailure.SubType);
            Assert.Equal(errorMessageUnknownFailure, webPushEventResult_DeviceTokenUnknownFailure.ErrorMessage);
        }

        [Fact]
        public async Task RegisterWebPushEventsAsync_ShouldLogError_ButReturnWebPushEvents_WhenServiceThrowAnException()
        {
            // Arrange
            Fixture fixture = new Fixture();
            var domain = fixture.Create<string>();
            var messageId = fixture.Create<Guid>();
            var deviceTokenValid = fixture.Create<string>();
            var exceptionMessage = "Testing exception";

            var sendMessageResult = new SendMessageResult()
            {
                SendMessageTargetResult = new List<SendMessageTargetResult>()
                {
                    new SendMessageTargetResult()
                    {
                        IsSuccess = true,
                        IsValidTargetDeviceToken = true,
                        TargetDeviceToken = deviceTokenValid,
                        NotSuccessErrorDetails = string.Empty,
                    }
                }
            };

            var mockWebPushEventRepository = new Mock<IWebPushEventRepository>();
            mockWebPushEventRepository
                .Setup(mwpr => mwpr.BulkInsertAsync(It.IsAny<IEnumerable<WebPushEvent>>()))
                .Throws(new Exception(exceptionMessage));

            var mockLogger = new Mock<ILogger<WebPushEventService>>();

            var sut = CreateSut(
                webPushEventRepository: mockWebPushEventRepository.Object,
                logger: mockLogger.Object
            );

            // Act
            var result = (await sut.RegisterWebPushEventsAsync(domain, messageId, sendMessageResult)).ToList();

            // Assert
            Assert.NotNull(result);
            Assert.Equal(sendMessageResult.SendMessageTargetResult.Count(), result.Count());

            mockLogger.Verify(
                x => x.Log(
                    It.Is<LogLevel>(l => l == LogLevel.Error),
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Unexpected error while registering WebPushEvents for domain:")),
                    It.Is<Exception>(e => e.Message == exceptionMessage),
                    (Func<It.IsAnyType, Exception, string>)It.IsAny<object>()
                ),
                Times.Once
            );
        }
    }
}
