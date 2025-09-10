using AutoFixture;
using Doppler.PushContact.DTOs;
using Doppler.PushContact.Models.DTOs;
using Doppler.PushContact.Models.Models;
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
using System.Linq;
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

        [Fact]
        public async Task ProcessWebPushInBatches_should_process_items_in_batches_based_on_configured_batch_size()
        {
            // Arrange
            var fixture = new Fixture();
            var domain = fixture.Create<string>();
            var messageId = fixture.Create<Guid>();
            var webPushDTO = new WebPushDTO
            {
                Title = fixture.Create<string>(),
                Body = fixture.Create<string>(),
                MessageId = messageId
            };

            var subscriptions = new List<SubscriptionInfoDTO>
            {
                // 3 con Subscription valida (deben entrar a ProcessWebPushBatchAsync)
                new() { Subscription = new SubscriptionDTO { EndPoint = "https://fcm.googleapis.com", Keys = new SubscriptionKeys { Auth = "a", P256DH = "b" } }, PushContactId = "1" },
                new() { Subscription = new SubscriptionDTO { EndPoint = "https://fcm.googleapis.com", Keys = new SubscriptionKeys { Auth = "c", P256DH = "d" } }, PushContactId = "2" },
                new() { Subscription = new SubscriptionDTO { EndPoint = "https://fcm.googleapis.com", Keys = new SubscriptionKeys { Auth = "e", P256DH = "f" } }, PushContactId = "3" },

                // 3 con DeviceToken (deben entrar a SendFirebaseWebPushAsync)
                new() { DeviceToken = "device1" },
                new() { DeviceToken = "device2" },
                new() { DeviceToken = "device3" },
            };

            var backgroundQueueMock = new Mock<IBackgroundQueue>();
            Func<CancellationToken, Task> capturedFunctionToBeSimulated = null;
            backgroundQueueMock
                .Setup(q => q.QueueBackgroundQueueItem(It.IsAny<Func<CancellationToken, Task>>()))
                .Callback<Func<CancellationToken, Task>>(func => capturedFunctionToBeSimulated = func);

            var pushContactRepositoryMock = new Mock<IPushContactRepository>();
            pushContactRepositoryMock.Setup(r => r.GetSubscriptionInfoByDomainAsStreamAsync(domain, It.IsAny<CancellationToken>()))
                .Returns(subscriptions.ToAsyncEnumerable());

            var messageSenderMock = new Mock<IMessageSender>();
            var webPushPublisherServiceMock = new Mock<WebPushPublisherService>(
                pushContactRepositoryMock.Object,
                backgroundQueueMock.Object,
                messageSenderMock.Object,
                Mock.Of<ILogger<WebPushPublisherService>>(),
                Mock.Of<IMessageQueuePublisher>(),
                Options.Create(new WebPushPublisherSettings { ProcessPushBatchSize = 2 }) // batch size 2
            )
            { CallBase = true };

            webPushPublisherServiceMock
                .Setup(x => x.ProcessWebPushBatchAsync(It.IsAny<IEnumerable<SubscriptionInfoDTO>>(), webPushDTO, It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask)
                .Verifiable();

            // Act
            webPushPublisherServiceMock.Object.ProcessWebPushInBatches(domain, webPushDTO, null);

            // Assert
            Assert.NotNull(capturedFunctionToBeSimulated);
            await capturedFunctionToBeSimulated(CancellationToken.None);

            // 3 subscriptions → 2 en primer batch, 1 en batch final → 2 llamadas
            webPushPublisherServiceMock.Verify(x =>
                x.ProcessWebPushBatchAsync(It.IsAny<IEnumerable<SubscriptionInfoDTO>>(), webPushDTO, It.IsAny<CancellationToken>()),
                Times.Exactly(2));

            // 3 deviceTokens → 2 en primer batch, 1 en final → 2 llamadas
            messageSenderMock.Verify(x =>
                x.SendFirebaseWebPushAsync(webPushDTO, It.Is<List<string>>(l => l.Count <= 2), null),
                Times.Exactly(2));
        }

        [Fact]
        public async Task ProcessWebPushInBatches_should_log_warning_and_do_nothing_when_cancelled_before_starting()
        {
            // Arrange
            var fixture = new Fixture();
            var domain = fixture.Create<string>();
            var webPushDTO = new WebPushDTO
            {
                Title = fixture.Create<string>(),
                Body = fixture.Create<string>(),
                MessageId = fixture.Create<Guid>()
            };

            var cancellationToken = new CancellationToken(canceled: true);

            var backgroundQueueMock = new Mock<IBackgroundQueue>();
            Func<CancellationToken, Task> capturedFunctionToBeSimulated = null;
            backgroundQueueMock
                .Setup(q => q.QueueBackgroundQueueItem(It.IsAny<Func<CancellationToken, Task>>()))
                .Callback<Func<CancellationToken, Task>>(func => capturedFunctionToBeSimulated = func);

            var pushContactRepositoryMock = new Mock<IPushContactRepository>();
            var messageSenderMock = new Mock<IMessageSender>();
            var loggerMock = new Mock<ILogger<WebPushPublisherService>>();
            var webPushQueueSettings = new WebPushPublisherSettings { ProcessPushBatchSize = 2 };

            var sut = CreateSut(
                pushContactRepository: pushContactRepositoryMock.Object,
                backgroundQueue: backgroundQueueMock.Object,
                messageSender: messageSenderMock.Object,
                messageQueuePublisher: Mock.Of<IMessageQueuePublisher>(),
                logger: loggerMock.Object,
                webPushQueueSettings: Options.Create(webPushQueueSettings)
            );

            // Act
            sut.ProcessWebPushInBatches(domain, webPushDTO, null);

            // Assert
            Assert.NotNull(capturedFunctionToBeSimulated);
            await capturedFunctionToBeSimulated(cancellationToken);

            // verifica que NO se llamaron metodos
            messageSenderMock.Verify(m => m.SendFirebaseWebPushAsync(It.IsAny<WebPushDTO>(), It.IsAny<List<string>>(), It.IsAny<string>()), Times.Never);
            pushContactRepositoryMock.Verify(r => r.GetSubscriptionInfoByDomainAsStreamAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);

            // verifica que se logueo el warning
            loggerMock.Verify(
                x => x.Log(
                    LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, _) => v.ToString().Contains("WebPush processing was cancelled before starting")),
                    null,
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }

        [Fact]
        public async Task ProcessWebPushInBatches_should_log_error_when_exception_occurs()
        {
            // Arrange
            var fixture = new Fixture();
            var domain = fixture.Create<string>();
            var webPushDTO = new WebPushDTO
            {
                Title = fixture.Create<string>(),
                Body = fixture.Create<string>(),
                MessageId = fixture.Create<Guid>()
            };

            var expectedException = new Exception("Something went wrong");

            var backgroundQueueMock = new Mock<IBackgroundQueue>();
            Func<CancellationToken, Task> capturedFunc = null;
            backgroundQueueMock
                .Setup(q => q.QueueBackgroundQueueItem(It.IsAny<Func<CancellationToken, Task>>()))
                .Callback<Func<CancellationToken, Task>>(func => capturedFunc = func);

            var pushContactRepositoryMock = new Mock<IPushContactRepository>();
            pushContactRepositoryMock
                .Setup(r => r.GetSubscriptionInfoByDomainAsStreamAsync(domain, It.IsAny<CancellationToken>()))
                .Throws(expectedException);

            var loggerMock = new Mock<ILogger<WebPushPublisherService>>();
            var messageSenderMock = new Mock<IMessageSender>();

            var sut = CreateSut(
                pushContactRepository: pushContactRepositoryMock.Object,
                backgroundQueue: backgroundQueueMock.Object,
                messageSender: messageSenderMock.Object,
                messageQueuePublisher: Mock.Of<IMessageQueuePublisher>(),
                logger: loggerMock.Object,
                webPushQueueSettings: Options.Create(new WebPushPublisherSettings())
            );

            // Act
            sut.ProcessWebPushInBatches(domain, webPushDTO, null);

            // Assert
            Assert.NotNull(capturedFunc);
            await capturedFunc(CancellationToken.None);

            // verifica que se logueo el error
            loggerMock.Verify(
                x => x.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, _) => v.ToString().Contains("An unexpected error occurred processing webpush")),
                    expectedException,
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }

        [Fact]
        public async Task ProcessWebPushForVisitors_should_log_warning_and_do_nothing_when_cancelled_before_starting()
        {
            // Arrange
            var fixture = new Fixture();
            var visitorGuid = fixture.Create<string>();
            var webPushDTO = new WebPushDTO
            {
                Title = fixture.Create<string>(),
                Body = fixture.Create<string>(),
                MessageId = fixture.Create<Guid>()
            };

            var cancellationToken = new CancellationToken(canceled: true);

            var backgroundQueueMock = new Mock<IBackgroundQueue>();
            Func<CancellationToken, Task> capturedFunctionToBeSimulated = null;
            backgroundQueueMock
                .Setup(q => q.QueueBackgroundQueueItem(It.IsAny<Func<CancellationToken, Task>>()))
                .Callback<Func<CancellationToken, Task>>(func => capturedFunctionToBeSimulated = func);

            var pushContactRepositoryMock = new Mock<IPushContactRepository>();
            var messageSenderMock = new Mock<IMessageSender>();
            var loggerMock = new Mock<ILogger<WebPushPublisherService>>();

            var sut = CreateSut(
                pushContactRepository: pushContactRepositoryMock.Object,
                backgroundQueue: backgroundQueueMock.Object,
                messageSender: messageSenderMock.Object,
                messageQueuePublisher: Mock.Of<IMessageQueuePublisher>(),
                logger: loggerMock.Object
            );

            // Act
            sut.ProcessWebPushForVisitors(webPushDTO, null, null);

            // Assert
            Assert.NotNull(capturedFunctionToBeSimulated);
            await capturedFunctionToBeSimulated(cancellationToken);

            // verifica que NO se llamaron metodos
            messageSenderMock.Verify(m => m.SendFirebaseWebPushAsync(It.IsAny<WebPushDTO>(), It.IsAny<List<string>>(), It.IsAny<string>()), Times.Never);
            pushContactRepositoryMock.Verify(r => r.GetAllSubscriptionInfoByVisitorGuidAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);

            // verifica que se logueo el warning
            loggerMock.Verify(
                x => x.Log(
                    LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, _) => v.ToString().Contains("WebPush processing was cancelled before starting")),
                    null,
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }

        [Fact]
        public async Task ProcessWebPushForVisitors_should_log_error_when_repository_throws_an_exception()
        {
            // Arrange
            var fixture = new Fixture();
            var visitorGuid = fixture.Create<string>();
            var domain = fixture.Create<string>();
            var webPushDTO = new WebPushDTO
            {
                Title = fixture.Create<string>(),
                Body = fixture.Create<string>(),
                MessageId = fixture.Create<Guid>(),
                Domain = domain,
            };

            var visitorsWithReplacements = new FieldsReplacementList()
            {
                ReplacementIsMandatory = false,
                VisitorsFieldsList = new List<VisitorFields>()
                    {
                        new VisitorFields{
                            VisitorGuid = visitorGuid,
                            Fields = new Dictionary<string, string> { { "field1", "value1" } },
                        },
                    },
            };

            var expectedException = new Exception("Something went wrong");

            var backgroundQueueMock = new Mock<IBackgroundQueue>();
            Func<CancellationToken, Task> capturedFunc = null;
            backgroundQueueMock
                .Setup(q => q.QueueBackgroundQueueItem(It.IsAny<Func<CancellationToken, Task>>()))
                .Callback<Func<CancellationToken, Task>>(func => capturedFunc = func);

            var pushContactRepositoryMock = new Mock<IPushContactRepository>();
            pushContactRepositoryMock
                .Setup(r => r.GetAllSubscriptionInfoByVisitorGuidAsync(domain, visitorGuid))
                .Throws(expectedException);

            var loggerMock = new Mock<ILogger<WebPushPublisherService>>();
            var messageSenderMock = new Mock<IMessageSender>();

            var sut = CreateSut(
                pushContactRepository: pushContactRepositoryMock.Object,
                backgroundQueue: backgroundQueueMock.Object,
                messageSender: messageSenderMock.Object,
                messageQueuePublisher: Mock.Of<IMessageQueuePublisher>(),
                logger: loggerMock.Object,
                webPushQueueSettings: Options.Create(new WebPushPublisherSettings())
            );

            // Act
            sut.ProcessWebPushForVisitors(webPushDTO, visitorsWithReplacements, null);

            // Assert
            Assert.NotNull(capturedFunc);
            await capturedFunc(CancellationToken.None);

            // verifica que se logueo el error
            loggerMock.Verify(
                x => x.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, _) => v.ToString().Contains("An unexpected error occurred processing webpush")),
                    expectedException,
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }

        [Fact]
        public async Task ProcessWebPushForVisitors_should_return_immediately_without_processing_when_visitors_list_is_null()
        {
            // Arrange
            // Arrange
            var fixture = new Fixture();
            var visitorGuid = fixture.Create<string>();
            var webPushDTO = new WebPushDTO
            {
                Title = fixture.Create<string>(),
                Body = fixture.Create<string>(),
                MessageId = fixture.Create<Guid>()
            };

            var visitorsWithReplacements = new FieldsReplacementList()
            {
                ReplacementIsMandatory = false,
                VisitorsFieldsList = null, // la lista de visitors (y fields) es null
            };

            var backgroundQueueMock = new Mock<IBackgroundQueue>();
            Func<CancellationToken, Task> capturedFunctionToBeSimulated = null;
            backgroundQueueMock
                .Setup(q => q.QueueBackgroundQueueItem(It.IsAny<Func<CancellationToken, Task>>()))
                .Callback<Func<CancellationToken, Task>>(func => capturedFunctionToBeSimulated = func);

            var pushContactRepositoryMock = new Mock<IPushContactRepository>();
            var messageSenderMock = new Mock<IMessageSender>();
            var loggerMock = new Mock<ILogger<WebPushPublisherService>>();

            var webPushPublisherServiceMock = new Mock<WebPushPublisherService>(
                pushContactRepositoryMock.Object,
                backgroundQueueMock.Object,
                messageSenderMock.Object,
                loggerMock.Object,
                Mock.Of<IMessageQueuePublisher>(),
                Options.Create(new WebPushPublisherSettings { ProcessPushBatchSize = 2 }) // batch size 2
            )
            { CallBase = true };

            // Act
            webPushPublisherServiceMock.Object.ProcessWebPushForVisitors(webPushDTO, visitorsWithReplacements, null);

            // Assert
            Assert.NotNull(capturedFunctionToBeSimulated);
            await capturedFunctionToBeSimulated(CancellationToken.None);

            // verifica que NO se llama al procesamiento de visitor
            webPushPublisherServiceMock.Verify(x =>
                x.ProcessWebPushForVisitorWithFields(webPushDTO, It.IsAny<VisitorFields>(), It.IsAny<bool>(), It.IsAny<CancellationToken>(), null),
                Times.Never);
        }

        [Fact]
        public async Task ProcessWebPushForVisitorWithFields_should_return_without_processing_when_replacement_is_mandatory_n_fields_are_missing()
        {
            // Arrange
            // Arrange
            var fixture = new Fixture();
            var visitorGuid = fixture.Create<string>();
            var webPushDTO = new WebPushDTO
            {
                Title = fixture.Create<string>(),
                Body = "body with fields: [[[fieldMissing]]], but it's missing to be replaced.",
                MessageId = fixture.Create<Guid>()
            };

            var visitorFields1 = new VisitorFields
            {
                VisitorGuid = visitorGuid,
                Fields = new Dictionary<string, string> { { "field1", "value1" } },
            };

            var visitorsWithReplacements = new FieldsReplacementList()
            {
                ReplacementIsMandatory = true,
                VisitorsFieldsList = new List<VisitorFields>()
                    {
                        visitorFields1,
                    },
            };

            var backgroundQueueMock = new Mock<IBackgroundQueue>();
            var pushContactRepositoryMock = new Mock<IPushContactRepository>();
            var messageSenderMock = new Mock<IMessageSender>();
            var loggerMock = new Mock<ILogger<WebPushPublisherService>>();

            var webPushPublisherServiceMock = new Mock<WebPushPublisherService>(
                pushContactRepositoryMock.Object,
                backgroundQueueMock.Object,
                messageSenderMock.Object,
                loggerMock.Object,
                Mock.Of<IMessageQueuePublisher>(),
                Options.Create(new WebPushPublisherSettings { ProcessPushBatchSize = 2 }) // batch size 2
            )
            { CallBase = true };

            // Act
            await webPushPublisherServiceMock.Object.ProcessWebPushForVisitorWithFields(
                webPushDTO,
                visitorFields1,
                visitorsWithReplacements.ReplacementIsMandatory,
                CancellationToken.None
            );

            // Assert
            // verifica que se logueo el warning
            loggerMock.Verify(
                x => x.Log(
                    LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, _) => v.ToString().Contains("Missing replacements for MessageId")),
                    null,
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);

            // verifica que NO se intentan obtener las subscripciones/tokens para el visitor
            pushContactRepositoryMock.Verify(r => r.GetAllSubscriptionInfoByVisitorGuidAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task ProcessWebPushForVisitorWithFields_should_log_error_when_repository_throws_an_exception()
        {
            // Arrange
            var fixture = new Fixture();
            var visitorGuid = fixture.Create<string>();
            var messageId = fixture.Create<Guid>();
            var domain = fixture.Create<string>();

            var webPushDTO = new WebPushDTO
            {
                Title = fixture.Create<string>(),
                Body = fixture.Create<string>(),
                MessageId = messageId,
                Domain = domain,
            };

            var visitorFields1 = new VisitorFields
            {
                VisitorGuid = visitorGuid,
                Fields = new Dictionary<string, string> { { "field1", "value1" } },
            };

            var visitorsWithReplacements = new FieldsReplacementList()
            {
                ReplacementIsMandatory = false,
                VisitorsFieldsList = new List<VisitorFields>()
                    {
                        visitorFields1,
                    },
            };

            var subscriptions = new List<SubscriptionInfoDTO>
            {
                // 1 con Subscription valida (deben encolar el mensaje)
                new() { Subscription = new SubscriptionDTO { EndPoint = "https://fcm.googleapis.com", Keys = new SubscriptionKeys { Auth = "a", P256DH = "b" } }, PushContactId = "1" },

                // 1 con DeviceToken (debe entrar a SendFirebaseWebPushAsync)
                new() { DeviceToken = "device1" },
            };

            var backgroundQueueMock = new Mock<IBackgroundQueue>();
            var pushContactRepositoryMock = new Mock<IPushContactRepository>();
            pushContactRepositoryMock
                .Setup(r => r.GetAllSubscriptionInfoByVisitorGuidAsync(domain, visitorGuid))
                .ReturnsAsync(subscriptions);

            var messageSenderMock = new Mock<IMessageSender>();
            var messageQueuePublisherMock = new Mock<IMessageQueuePublisher>();
            var loggerMock = new Mock<ILogger<WebPushPublisherService>>();

            var webPushPublisherServiceMock = new Mock<WebPushPublisherService>(
                pushContactRepositoryMock.Object,
                backgroundQueueMock.Object,
                messageSenderMock.Object,
                loggerMock.Object,
                messageQueuePublisherMock.Object,
                Options.Create(webPushQueueSettingsDefault)
            )
            { CallBase = true };

            // Act
            await webPushPublisherServiceMock.Object.ProcessWebPushForVisitorWithFields(
                webPushDTO,
                visitorFields1,
                visitorsWithReplacements.ReplacementIsMandatory,
                CancellationToken.None
            );

            // Assert
            // verify enqueueing message
            messageQueuePublisherMock.Verify(
                q => q.PublishAsync(
                    It.Is<WebPushDTO>(dto => dto.MessageId == messageId),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()
                ),
                Times.Once
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
    }
}
