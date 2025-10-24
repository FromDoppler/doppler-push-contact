using AutoFixture;
using Doppler.PushContact.Models.DTOs;
using Doppler.PushContact.Models.Entities;
using Doppler.PushContact.Models.Enums;
using Doppler.PushContact.QueuingService.MessageQueueBroker;
using Doppler.PushContact.WebPushSender.DTOs;
using Doppler.PushContact.WebPushSender.Repositories.Interfaces;
using Doppler.PushContact.WebPushSender.Senders;
using Doppler.PushContact.WebPushSender.Test.Senders.Dummies;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Doppler.PushContact.WebPushSender.Test.Senders
{
    public class DefaultWebPushSenderTest
    {
        private static readonly WebPushSenderSettings webPushSenderSettingsDefault =
            new WebPushSenderSettings
            {
                PushApiUrl = "https://api.push.com",
                QueueName = "test.queue"
            };

        private static TestableDefaultWebPushSender CreateSUT(
            IOptions<WebPushSenderSettings> webPushSenderSettings = null,
            IMessageQueueSubscriber messageQueueSubscriber = null,
            ILogger<DefaultWebPushSender> logger = null,
            IWebPushEventRepository webPushEventRepository = null,
            IPushContactRepository pushContactRepository = null,
            IMessageRepository messageRepository = null,
            IMessageStatsRepository messageStatsRepository = null,
            SendWebPushDelegate sendWebPushDelegate = null
            )
        {
            return new TestableDefaultWebPushSender(
                webPushSenderSettings ?? Options.Create(webPushSenderSettingsDefault),
                messageQueueSubscriber ?? Mock.Of<IMessageQueueSubscriber>(),
                logger ?? Mock.Of<ILogger<DefaultWebPushSender>>(),
                webPushEventRepository ?? Mock.Of<IWebPushEventRepository>(),
                pushContactRepository ?? Mock.Of<IPushContactRepository>(),
                messageRepository ?? Mock.Of<IMessageRepository>(),
                messageStatsRepository ?? Mock.Of<IMessageStatsRepository>(),
                sendWebPushDelegate ?? Mock.Of<SendWebPushDelegate>()
            );
        }

        [Theory]
        [InlineData(true, false, false, false, false, WebPushEventType.ProcessingFailed)]
        [InlineData(false, true, false, false, false, WebPushEventType.Delivered)]
        [InlineData(false, false, true, false, false, WebPushEventType.DeliveryFailed)]
        [InlineData(false, false, false, true, false, WebPushEventType.DeliveryFailedButRetry)]
        [InlineData(false, false, false, false, true, WebPushEventType.DeliveryFailed)]
        public async Task HandleMessageAsync_Should_Call_Repository_With_Expected_EventType(
            bool failedProcessing,
            bool successfullyDelivered,
            bool invalidSubscription,
            bool limitsExceeded,
            bool unknownFail,
            WebPushEventType expectedEventType
        )
        {
            // Arrange
            Fixture fixture = new Fixture();

            var messageId = fixture.Create<Guid>();
            var pushContactId = fixture.Create<string>();
            var errorMessage = fixture.Create<string>();

            var processingResult = new WebPushProcessingResultDTO
            {
                FailedProcessing = failedProcessing,
                SuccessfullyDelivered = successfullyDelivered,
                InvalidSubscription = invalidSubscription,
                LimitsExceeded = limitsExceeded,
                UnknownFail = unknownFail,
            };

            if (unknownFail)
            {
                processingResult.ErrorMessage = errorMessage;
            }

            var weshPushEventRepository = new Mock<IWebPushEventRepository>();
            SendWebPushDelegate delegateWithBehavior = _ => Task.FromResult(processingResult);

            var sender = CreateSUT(
                webPushEventRepository: weshPushEventRepository.Object,
                sendWebPushDelegate: delegateWithBehavior
            );

            var message = new DopplerWebPushDTO
            {
                MessageId = messageId,
                PushContactId = pushContactId,
            };

            var cancellationToken = CancellationToken.None;

            // Act
            await sender.HandleMessageAsync(message);

            // Assert
            weshPushEventRepository.Verify(repo => repo.InsertAsync(
                It.Is<WebPushEvent>(evt =>
                    evt.MessageId == messageId &&
                    evt.PushContactId == pushContactId &&
                    evt.Type == (int)expectedEventType &&
                    evt.ErrorMessage == (unknownFail ? errorMessage : null)
                ),
                cancellationToken
            ), Times.Once);
        }
    }
}
