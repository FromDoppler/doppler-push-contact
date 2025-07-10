using AutoFixture;
using Doppler.PushContact.DTOs;
using Doppler.PushContact.Models.DTOs;
using Doppler.PushContact.QueuingService.MessageQueueBroker;
using Doppler.PushContact.Repositories.Interfaces;
using Doppler.PushContact.Services;
using Doppler.PushContact.Services.Messages;
using Doppler.PushContact.Services.Queue;
using Doppler.PushContact.Transversal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Doppler.PushContact.Test.Services
{
    public class TestQueueBackgroundService : QueueBackgroundService
    {
        public TestQueueBackgroundService(IBackgroundQueue backgroundQueue)
            : base(backgroundQueue)
        {
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // overwrite the method to do nothing on the tests
            await Task.CompletedTask;
        }
    }

    public class WebPushPublisherServiceTest
    {
        public WebPushPublisherServiceTest()
        {
            var TestKey = "5Rz2VJbnjbhPfEKn3Ryd0E+u7jzOT2KCBicmM5wUq5Y=";
            var TestIV = "7yZ8kT8L7UeO8JpH3Ir6jQ==";
            EncryptionHelper.Initialize(TestKey, TestIV);
        }

        private static readonly WebPushPublisherSettings webPushQueueSettingsDefault =
            new WebPushPublisherSettings
            {
                PushEndpointMappings = new Dictionary<string, List<string>>
                {
                    { "google", new List<string> { "https://fcm.googleapis.com" } },
                    { "mozilla", new List<string> { "https://updates.push.services.mozilla.com" } },
                    { "microsoft", new List<string> { "https://wns.windows.com" } },
                    { "apple", new List<string> { "https://api.push.apple.com" } }
                }
            };

        private const string QUEUE_NAME_SUFIX = "webpush.queue";
        private const string DEFAULT_QUEUE_NAME = $"default.{QUEUE_NAME_SUFIX}";

        private static WebPushPublisherService CreateSut(
            IPushContactRepository pushContactRepository = null,
            IBackgroundQueue backgroundQueue = null,
            IMessageSender messageSender = null,
            ILogger<WebPushPublisherService> logger = null,
            IMessageQueuePublisher messageQueuePublisher = null,
            IOptions<WebPushPublisherSettings> webPushQueueSettings = null
        )
        {
            return new WebPushPublisherService(
                pushContactRepository ?? Mock.Of<IPushContactRepository>(),
                backgroundQueue ?? Mock.Of<IBackgroundQueue>(),
                messageSender ?? Mock.Of<IMessageSender>(),
                logger ?? Mock.Of<ILogger<WebPushPublisherService>>(),
                messageQueuePublisher ?? Mock.Of<IMessageQueuePublisher>(),
                webPushQueueSettings ?? Options.Create(webPushQueueSettingsDefault)
            );
        }

        [Theory]
        [InlineData("https://unknown.service.api.com", DEFAULT_QUEUE_NAME)]
        [InlineData("https://fcm.googleapis.com/path", $"google.{QUEUE_NAME_SUFIX}")]
        [InlineData("https://updates.push.services.mozilla.com", $"mozilla.{QUEUE_NAME_SUFIX}")]
        [InlineData("https://wns.windows.com", $"microsoft.{QUEUE_NAME_SUFIX}")]
        [InlineData("https://api.push.apple.com", $"apple.{QUEUE_NAME_SUFIX}")]
        public async Task ProcessWebPush_should_finish_ok_pushing_subscriptions_in_proper_queue_and_calling_SendFirebase_but_without_items(
            string endpoint,
            string expectedQueueName
        )
        {
            // Arrange
            var fixture = new Fixture();

            var domain = fixture.Create<string>();
            var title = fixture.Create<string>();
            var body = fixture.Create<string>();
            var messageId = fixture.Create<Guid>();
            var webPushDTO = new WebPushDTO()
            {
                Title = title,
                Body = body,
                MessageId = messageId
            };

            var backgroundQueueMock = new Mock<IBackgroundQueue>();
            Func<CancellationToken, Task> capturedFunctionToBeSimulated = null;

            backgroundQueueMock
                .Setup(q => q.QueueBackgroundQueueItem(It.IsAny<Func<CancellationToken, Task>>()))
                .Callback<Func<CancellationToken, Task>>(func => capturedFunctionToBeSimulated = func);

            var pushContactRepositoryMock = new Mock<IPushContactRepository>();
            var subscriptions = new List<SubscriptionInfoDTO>
            {
                new SubscriptionInfoDTO
                {
                    Subscription = new SubscriptionDTO
                    {
                        EndPoint = endpoint,
                        Keys = new SubscriptionKeys
                        {
                            Auth = "auth",
                            P256DH = "p256dh"
                        }
                    }
                },
            };

            pushContactRepositoryMock
                .Setup(s => s.GetAllSubscriptionInfoByDomainAsync(domain))
                .ReturnsAsync(subscriptions);

            var messageSenderMock = new Mock<IMessageSender>();
            var messageQueuePublisherMock = new Mock<IMessageQueuePublisher>();

            var sut = CreateSut(
                pushContactRepository: pushContactRepositoryMock.Object,
                backgroundQueue: backgroundQueueMock.Object,
                messageSender: messageSenderMock.Object,
                messageQueuePublisher: messageQueuePublisherMock.Object
            );

            // Act
            sut.ProcessWebPush(domain, webPushDTO, null);

            // Assert
            Assert.NotNull(capturedFunctionToBeSimulated);

            // simulate the captured function execution
            await capturedFunctionToBeSimulated(CancellationToken.None);

            messageQueuePublisherMock.Verify(
                q => q.PublishAsync(
                    It.Is<WebPushDTO>(dto => dto.MessageId == messageId),
                    expectedQueueName,
                    CancellationToken.None
                ),
                Times.Once
            );

            // verify calling to SendFirebaseWebPushAsync but passing an empty list
            messageSenderMock.Verify(
                s => s.SendFirebaseWebPushAsync(
                    It.Is<WebPushDTO>(dto => dto.MessageId == messageId),
                    It.Is<List<string>>(x => x.Count == 0),
                    null),
                Times.Once
            );
        }

        [Theory]
        [InlineData(null, "auth", "p256dh")]
        [InlineData("", "auth", "p256dh")]
        [InlineData("endpoint", null, "p256dh")]
        [InlineData("endpoint", "", "p256dh")]
        [InlineData("endpoint", "auth", null)]
        [InlineData("endpoint", "auth", "")]
        public async Task ProcessWebPush_should_finish_ok_calling_SendFirebase_with_items_and_no_considering_wrong_subscriptions(
            string endpoint,
            string auth,
            string p256dh
        )
        {
            // Arrange
            var fixture = new Fixture();

            var domain = fixture.Create<string>();
            var title = fixture.Create<string>();
            var body = fixture.Create<string>();
            var messageId = fixture.Create<Guid>();
            var webPushDTO = new WebPushDTO()
            {
                Title = title,
                Body = body,
                MessageId = messageId
            };

            var backgroundQueueMock = new Mock<IBackgroundQueue>();
            Func<CancellationToken, Task> capturedFunctionToBeSimulated = null;

            backgroundQueueMock
                .Setup(q => q.QueueBackgroundQueueItem(It.IsAny<Func<CancellationToken, Task>>()))
                .Callback<Func<CancellationToken, Task>>(func => capturedFunctionToBeSimulated = func);

            var pushContactRepositoryMock = new Mock<IPushContactRepository>();
            var subscriptions = new List<SubscriptionInfoDTO>
            {
                new SubscriptionInfoDTO
                {
                    Subscription = new SubscriptionDTO
                    {
                        EndPoint = endpoint,
                        Keys = new SubscriptionKeys
                        {
                            Auth = auth,
                            P256DH = p256dh
                        }
                    },
                    DeviceToken = "deviceToken"
                },
            };

            pushContactRepositoryMock
                .Setup(s => s.GetAllSubscriptionInfoByDomainAsync(domain))
                .ReturnsAsync(subscriptions);

            var messageSenderMock = new Mock<IMessageSender>();
            var messageQueuePublisherMock = new Mock<IMessageQueuePublisher>();

            var sut = CreateSut(
                pushContactRepository: pushContactRepositoryMock.Object,
                backgroundQueue: backgroundQueueMock.Object,
                messageSender: messageSenderMock.Object,
                messageQueuePublisher: messageQueuePublisherMock.Object
            );

            // Act
            sut.ProcessWebPush(domain, webPushDTO, null);

            // Assert
            Assert.NotNull(capturedFunctionToBeSimulated);

            // simulate the captured function execution
            await capturedFunctionToBeSimulated(CancellationToken.None);

            messageQueuePublisherMock.Verify(
                q => q.PublishAsync(
                    It.IsAny<WebPushDTO>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()
                ),
                Times.Never
            );

            // verify calling to SendFirebaseWebPushAsync passing a list with a item
            messageSenderMock.Verify(
                s => s.SendFirebaseWebPushAsync(
                    It.Is<WebPushDTO>(dto => dto.MessageId == messageId),
                    It.Is<List<string>>(x => x.Count == 1),
                    null),
                Times.Once
            );
        }

        [Fact]
        public async Task ProcessWebPush_should_catch_and_log_unexpected_exception()
        {
            // Arrange
            var fixture = new Fixture();

            var domain = fixture.Create<string>();
            var title = fixture.Create<string>();
            var body = fixture.Create<string>();
            var messageId = fixture.Create<Guid>();
            var webPushDTO = new WebPushDTO()
            {
                Title = title,
                Body = body,
                MessageId = messageId
            };

            var backgroundQueueMock = new Mock<IBackgroundQueue>();
            Func<CancellationToken, Task> capturedFunctionToBeSimulated = null;

            backgroundQueueMock
                .Setup(q => q.QueueBackgroundQueueItem(It.IsAny<Func<CancellationToken, Task>>()))
                .Callback<Func<CancellationToken, Task>>(func => capturedFunctionToBeSimulated = func);

            var pushContactRepositoryMock = new Mock<IPushContactRepository>();
            pushContactRepositoryMock
                .Setup(s => s.GetAllSubscriptionInfoByDomainAsync(domain))
                .Throws(new Exception());

            var loggerMock = new Mock<ILogger<WebPushPublisherService>>();

            var sut = CreateSut(
                pushContactRepository: pushContactRepositoryMock.Object,
                backgroundQueue: backgroundQueueMock.Object,
                logger: loggerMock.Object
            );

            // Act
            sut.ProcessWebPush(domain, webPushDTO, null);

            // Assert
            Assert.NotNull(capturedFunctionToBeSimulated);

            // simulate the captured function execution
            await capturedFunctionToBeSimulated(CancellationToken.None);

            loggerMock.Verify(
                x => x.Log(
                    It.Is<LogLevel>(l => l == LogLevel.Error),
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains($"An unexpected error occurred processing webpush for domain: {domain}")),
                    It.IsAny<Exception>(),
                    It.Is<Func<It.IsAnyType, Exception, string>>((v, t) => true)),
                Times.Once);
        }

        [Fact]
        public async Task ProcessWebPush_should_invoke_to_internal_EnqueueWebPushAsync_and_it_should_catch_and_log_unexpected_exception()
        {
            // Arrange
            var fixture = new Fixture();

            var domain = fixture.Create<string>();
            var title = fixture.Create<string>();
            var body = fixture.Create<string>();
            var messageId = fixture.Create<Guid>();
            var webPushDTO = new WebPushDTO()
            {
                Title = title,
                Body = body,
                MessageId = messageId
            };

            var backgroundQueueMock = new Mock<IBackgroundQueue>();
            Func<CancellationToken, Task> capturedFunctionToBeSimulated = null;

            backgroundQueueMock
                .Setup(q => q.QueueBackgroundQueueItem(It.IsAny<Func<CancellationToken, Task>>()))
                .Callback<Func<CancellationToken, Task>>(func => capturedFunctionToBeSimulated = func);

            var pushContactRepositoryMock = new Mock<IPushContactRepository>();
            var subscriptions = new List<SubscriptionInfoDTO>
            {
                new SubscriptionInfoDTO
                {
                    Subscription = new SubscriptionDTO
                    {
                        EndPoint = "endpoint",
                        Keys = new SubscriptionKeys
                        {
                            Auth = "auth",
                            P256DH = "p256dh"
                        }
                    }
                },
            };

            pushContactRepositoryMock
                .Setup(s => s.GetAllSubscriptionInfoByDomainAsync(domain))
                .ReturnsAsync(subscriptions);

            var messageQueuePublisherMock = new Mock<IMessageQueuePublisher>();
            messageQueuePublisherMock
                .Setup(mqp => mqp.PublishAsync(It.IsAny<DopplerWebPushDTO>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Throws(new Exception());

            var loggerMock = new Mock<ILogger<WebPushPublisherService>>();

            var sut = CreateSut(
                pushContactRepository: pushContactRepositoryMock.Object,
                backgroundQueue: backgroundQueueMock.Object,
                messageQueuePublisher: messageQueuePublisherMock.Object,
                logger: loggerMock.Object
            );

            // Act
            sut.ProcessWebPush(domain, webPushDTO, null);

            // Assert
            Assert.NotNull(capturedFunctionToBeSimulated);

            // simulate the captured function execution
            await capturedFunctionToBeSimulated(CancellationToken.None);

            loggerMock.Verify(
                x => x.Log(
                    It.Is<LogLevel>(l => l == LogLevel.Error),
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains($"An unexpected error occurred enqueuing webpush for messageId: {webPushDTO.MessageId}")),
                    It.IsAny<Exception>(),
                    It.Is<Func<It.IsAnyType, Exception, string>>((v, t) => true)),
                Times.Once);
        }

        [Fact]
        public async Task ProcessWebPush_should_finish_ok_pushing_a_subscription_with_defined_clicked_n_received_endpoints()
        {
            // Arrange
            var fixture = new Fixture();

            var domain = fixture.Create<string>();
            var title = fixture.Create<string>();
            var body = fixture.Create<string>();
            var messageId = fixture.Create<Guid>();

            var subscriptionEndpoint = "https://example.com/endpoint";
            var queueNamePrefix = "example";

            var webPushDTO = new WebPushDTO()
            {
                Title = title,
                Body = body,
                MessageId = messageId
            };

            var backgroundQueueMock = new Mock<IBackgroundQueue>();
            Func<CancellationToken, Task> capturedFunctionToBeSimulated = null;

            backgroundQueueMock
                .Setup(q => q.QueueBackgroundQueueItem(It.IsAny<Func<CancellationToken, Task>>()))
                .Callback<Func<CancellationToken, Task>>(func => capturedFunctionToBeSimulated = func);

            var pushContactRepositoryMock = new Mock<IPushContactRepository>();
            var subscriptions = new List<SubscriptionInfoDTO>
            {
                new SubscriptionInfoDTO
                {
                    Subscription = new SubscriptionDTO
                    {
                        EndPoint = subscriptionEndpoint,
                        Keys = new SubscriptionKeys
                        {
                            Auth = "auth",
                            P256DH = "p256dh"
                        }
                    },
                    PushContactId = "aContactId",
                }
            };

            pushContactRepositoryMock
                .Setup(s => s.GetAllSubscriptionInfoByDomainAsync(domain))
                .ReturnsAsync(subscriptions);

            var webPushQueueSettings = new WebPushPublisherSettings
            {
                PushEndpointMappings = new Dictionary<string, List<string>>
                {
                    { queueNamePrefix, new List<string> { subscriptionEndpoint } }
                },
                PushContactApiUrl = "https://push.api.test",
                ClickedEventEndpointPath = "[pushContactApiUrl]/push-contacts/[encryptedContactId]/messages/[encryptedMessageId]/clicked",
                ReceivedEventEndpointPath = "[pushContactApiUrl]/push-contacts/[encryptedContactId]/messages/[encryptedMessageId]/received"
            };

            var messageQueuePublisherMock = new Mock<IMessageQueuePublisher>();
            var messageSenderMock = new Mock<IMessageSender>();
            var loggerMock = new Mock<ILogger<WebPushPublisherService>>();

            var sut = CreateSut(
                pushContactRepository: pushContactRepositoryMock.Object,
                backgroundQueue: backgroundQueueMock.Object,
                messageSender: messageSenderMock.Object,
                messageQueuePublisher: messageQueuePublisherMock.Object,
                logger: loggerMock.Object,
                webPushQueueSettings: Options.Create(webPushQueueSettings)
            );

            // Act
            sut.ProcessWebPush(domain, webPushDTO, null);

            // Assert
            Assert.NotNull(capturedFunctionToBeSimulated);

            // simulate the captured function execution
            await capturedFunctionToBeSimulated(CancellationToken.None);

            messageQueuePublisherMock.Verify(
                q => q.PublishAsync(
                    It.Is<DopplerWebPushDTO>(dto => dto.MessageId == messageId && dto.ClickedEventEndpoint != null && dto.ReceivedEventEndpoint != null),
                    It.Is<string>(queue => queue == $"{queueNamePrefix}.webpush.queue"),
                    It.IsAny<CancellationToken>()
                ),
                Times.Once
            );
        }

        [Theory]
        [InlineData(null, "[pushContactApiUrl]/clicked", "[pushContactApiUrl]/received", "aContactId")]
        [InlineData("", "[pushContactApiUrl]/clicked", "[pushContactApiUrl]/received", "aContactId")]
        [InlineData("pushContactApiUrl", null, "[pushContactApiUrl]/received", "aContactId")]
        [InlineData("pushContactApiUrl", "", "[pushContactApiUrl]/received", "aContactId")]
        [InlineData("pushContactApiUrl", "[pushContactApiUrl]/clicked", null, "aContactId")]
        [InlineData("pushContactApiUrl", "[pushContactApiUrl]/clicked", "", "aContactId")]
        [InlineData("pushContactApiUrl", "[pushContactApiUrl]/clicked", "[pushContactApiUrl]/received", null)]
        [InlineData("pushContactApiUrl", "[pushContactApiUrl]/clicked", "[pushContactApiUrl]/received", "")]
        public async Task ProcessWebPush_should_finish_ok_pushing_a_subscription_with_undefined_clicked_n_received_endpoints(
            string pushContactApiUrl,
            string clickedEventEndpoint,
            string receivedEventEndpoint,
            string contactId
        )
        {
            // Arrange
            var fixture = new Fixture();

            var domain = fixture.Create<string>();
            var title = fixture.Create<string>();
            var body = fixture.Create<string>();
            var messageId = fixture.Create<Guid>();

            var subscriptionEndpoint = "https://example.com/endpoint";
            var queueNamePrefix = "example";

            var webPushDTO = new WebPushDTO()
            {
                Title = title,
                Body = body,
                MessageId = messageId
            };

            var backgroundQueueMock = new Mock<IBackgroundQueue>();
            Func<CancellationToken, Task> capturedFunctionToBeSimulated = null;

            backgroundQueueMock
                .Setup(q => q.QueueBackgroundQueueItem(It.IsAny<Func<CancellationToken, Task>>()))
                .Callback<Func<CancellationToken, Task>>(func => capturedFunctionToBeSimulated = func);

            var pushContactRepositoryMock = new Mock<IPushContactRepository>();
            var subscriptions = new List<SubscriptionInfoDTO>
            {
                new SubscriptionInfoDTO
                {
                    Subscription = new SubscriptionDTO
                    {
                        EndPoint = subscriptionEndpoint,
                        Keys = new SubscriptionKeys
                        {
                            Auth = "auth",
                            P256DH = "p256dh"
                        }
                    },
                    PushContactId = contactId,
                }
            };

            pushContactRepositoryMock
                .Setup(s => s.GetAllSubscriptionInfoByDomainAsync(domain))
                .ReturnsAsync(subscriptions);

            var webPushQueueSettings = new WebPushPublisherSettings
            {
                PushEndpointMappings = new Dictionary<string, List<string>>
                {
                    { queueNamePrefix, new List<string> { subscriptionEndpoint } }
                },
                PushContactApiUrl = pushContactApiUrl,
                ClickedEventEndpointPath = clickedEventEndpoint,
                ReceivedEventEndpointPath = receivedEventEndpoint,
            };

            var messageQueuePublisherMock = new Mock<IMessageQueuePublisher>();
            var messageSenderMock = new Mock<IMessageSender>();
            var loggerMock = new Mock<ILogger<WebPushPublisherService>>();

            var sut = CreateSut(
                pushContactRepository: pushContactRepositoryMock.Object,
                backgroundQueue: backgroundQueueMock.Object,
                messageSender: messageSenderMock.Object,
                messageQueuePublisher: messageQueuePublisherMock.Object,
                logger: loggerMock.Object,
                webPushQueueSettings: Options.Create(webPushQueueSettings)
            );

            // Act
            sut.ProcessWebPush(domain, webPushDTO, null);

            // Assert
            Assert.NotNull(capturedFunctionToBeSimulated);

            // simulate the captured function execution
            await capturedFunctionToBeSimulated(CancellationToken.None);

            messageQueuePublisherMock.Verify(
                q => q.PublishAsync(
                    It.Is<DopplerWebPushDTO>(dto => dto.MessageId == messageId &&
                        (dto.ClickedEventEndpoint == null || dto.ReceivedEventEndpoint == null)
                    ),
                    It.Is<string>(queue => queue == $"{queueNamePrefix}.webpush.queue"),
                    It.IsAny<CancellationToken>()
                ),
                Times.Once
            );
        }
    }
}
