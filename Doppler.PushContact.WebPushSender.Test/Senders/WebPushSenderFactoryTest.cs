using Doppler.PushContact.QueuingService.MessageQueueBroker;
using Doppler.PushContact.WebPushSender.Repositories.Interfaces;
using Doppler.PushContact.WebPushSender.Senders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using System;
using Xunit;

namespace Doppler.PushContact.WebPushSender.Test.Senders
{
    public class WebPushSenderFactoryTests
    {
        private static WebPushSenderFactory CreateSUT(
            IServiceProvider serviceProvider = null
        )
        {
            return new WebPushSenderFactory(
                serviceProvider ?? Mock.Of<IServiceProvider>()
            );
        }

        public class LoggerFactoryMock : ILoggerFactory
        {
            private readonly ILogger _logger;

            public LoggerFactoryMock(ILogger logger)
            {
                _logger = logger;
            }

            public void AddProvider(ILoggerProvider provider) { }

            public ILogger CreateLogger(string categoryName)
            {
                return _logger;
            }

            public void Dispose() { }
        }

        [Fact]
        public void CreateSender_Should_Return_DefaultWebPushSender_When_Type_Is_Default()
        {
            // Arrange
            var serviceProviderMock = new Mock<IServiceProvider>();
            var messageQueueSubscriberMock = new Mock<IMessageQueueSubscriber>();
            var loggerMock = new Mock<ILogger<DefaultWebPushSender>>();
            var webPushEventRepository = new Mock<IWebPushEventRepository>();
            var pushContactRepository = new Mock<IPushContactRepository>();
            var messageRepository = new Mock<IMessageRepository>();
            var messageStatsRepository = new Mock<IMessageStatsRepository>();

            serviceProviderMock
                .Setup(sp => sp.GetService(typeof(IMessageQueueSubscriber)))
                .Returns(messageQueueSubscriberMock.Object);

            serviceProviderMock
                .Setup(sp => sp.GetService(typeof(ILoggerFactory)))
                .Returns(new LoggerFactoryMock(loggerMock.Object));

            serviceProviderMock
                .Setup(sp => sp.GetService(typeof(IWebPushEventRepository)))
                .Returns(webPushEventRepository.Object);

            serviceProviderMock
                .Setup(sp => sp.GetService(typeof(IPushContactRepository)))
                .Returns(pushContactRepository.Object);

            serviceProviderMock
                .Setup(sp => sp.GetService(typeof(IMessageRepository)))
                .Returns(messageRepository.Object);

            serviceProviderMock
                .Setup(sp => sp.GetService(typeof(IMessageStatsRepository)))
                .Returns(messageStatsRepository.Object);

            var webPushSenderSettings = Options.Create(new WebPushSenderSettings
            {
                Type = WebPushSenderTypes.Default
            });

            var webPushSenderFactory = CreateSUT(serviceProviderMock.Object);

            // Act
            var sender = webPushSenderFactory.CreateSender(webPushSenderSettings);

            // Assert
            Assert.NotNull(sender);
            Assert.IsType<DefaultWebPushSender>(sender);
        }

        [Fact]
        public void CreateSender_Should_Return_DefaultWebPushSender_For_Unknown_Type()
        {
            // Arrange
            var serviceProviderMock = new Mock<IServiceProvider>();
            var messageQueueSubscriberMock = new Mock<IMessageQueueSubscriber>();
            var loggerMock = new Mock<ILogger<DefaultWebPushSender>>();
            var webPushEventRepository = new Mock<IWebPushEventRepository>();
            var pushContactRepository = new Mock<IPushContactRepository>();
            var messageRepository = new Mock<IMessageRepository>();
            var messageStatsRepository = new Mock<IMessageStatsRepository>();

            serviceProviderMock
                .Setup(sp => sp.GetService(typeof(IMessageQueueSubscriber)))
                .Returns(messageQueueSubscriberMock.Object);

            serviceProviderMock
                .Setup(sp => sp.GetService(typeof(ILoggerFactory)))
                .Returns(new LoggerFactoryMock(loggerMock.Object));

            serviceProviderMock
                .Setup(sp => sp.GetService(typeof(IWebPushEventRepository)))
                .Returns(webPushEventRepository.Object);

            serviceProviderMock
                .Setup(sp => sp.GetService(typeof(IPushContactRepository)))
                .Returns(pushContactRepository.Object);

            serviceProviderMock
                .Setup(sp => sp.GetService(typeof(IMessageRepository)))
                .Returns(messageRepository.Object);

            serviceProviderMock
                .Setup(sp => sp.GetService(typeof(IMessageStatsRepository)))
                .Returns(messageStatsRepository.Object);

            var webPushSenderSettings = Options.Create(new WebPushSenderSettings
            {
                Type = (WebPushSenderTypes)999 // Unknown type
            });

            var webPushSenderFactory = CreateSUT(serviceProviderMock.Object);

            // Act
            var sender = webPushSenderFactory.CreateSender(webPushSenderSettings);

            // Assert
            Assert.NotNull(sender);
            Assert.IsType<DefaultWebPushSender>(sender);
        }

        [Theory]
        [InlineData(WebPushSenderTypes.Default)]
        [InlineData(WebPushSenderTypes.Apple)]
        [InlineData(WebPushSenderTypes.Google)]
        [InlineData(WebPushSenderTypes.Mozilla)]
        [InlineData(WebPushSenderTypes.Windows)]
        public void CreateSender_Should_Return_DefaultWebPushSender_When_For_All_WebPushSenderTypes(WebPushSenderTypes type)
        {
            // Arrange
            var serviceProviderMock = new Mock<IServiceProvider>();
            var messageQueueSubscriberMock = new Mock<IMessageQueueSubscriber>();
            var loggerMock = new Mock<ILogger<DefaultWebPushSender>>();
            var webPushEventRepository = new Mock<IWebPushEventRepository>();
            var pushContactRepository = new Mock<IPushContactRepository>();
            var messageRepository = new Mock<IMessageRepository>();
            var messageStatsRepository = new Mock<IMessageStatsRepository>();

            serviceProviderMock
                .Setup(sp => sp.GetService(typeof(IMessageQueueSubscriber)))
                .Returns(messageQueueSubscriberMock.Object);

            serviceProviderMock
                .Setup(sp => sp.GetService(typeof(ILoggerFactory)))
                .Returns(new LoggerFactoryMock(loggerMock.Object));

            serviceProviderMock
                .Setup(sp => sp.GetService(typeof(IWebPushEventRepository)))
                .Returns(webPushEventRepository.Object);

            serviceProviderMock
                .Setup(sp => sp.GetService(typeof(IPushContactRepository)))
                .Returns(pushContactRepository.Object);

            serviceProviderMock
                .Setup(sp => sp.GetService(typeof(IMessageRepository)))
                .Returns(messageRepository.Object);

            serviceProviderMock
                .Setup(sp => sp.GetService(typeof(IMessageStatsRepository)))
                .Returns(messageStatsRepository.Object);

            var webPushSenderSettings = Options.Create(new WebPushSenderSettings
            {
                Type = type
            });

            var webPushSenderFactory = CreateSUT(serviceProviderMock.Object);

            // Act
            var sender = webPushSenderFactory.CreateSender(webPushSenderSettings);

            // Assert
            Assert.NotNull(sender);
            Assert.IsType<DefaultWebPushSender>(sender);
        }
    }
}
