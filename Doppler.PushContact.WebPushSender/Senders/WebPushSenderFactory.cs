using Doppler.PushContact.QueuingService.MessageQueueBroker;
using Doppler.PushContact.WebPushSender.Repositories.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;

namespace Doppler.PushContact.WebPushSender.Senders
{
    public class WebPushSenderFactory : IWebPushSenderFactory
    {
        private readonly IServiceProvider _serviceProvider;

        public WebPushSenderFactory(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public IWebPushSender CreateSender(IOptions<WebPushSenderSettings> webPushSenderSettings)
        {
            var messageQueueSubscriber = _serviceProvider.GetRequiredService<IMessageQueueSubscriber>();
            var loggerFactory = _serviceProvider.GetRequiredService<ILoggerFactory>();
            var webPushEventRepository = _serviceProvider.GetRequiredService<IWebPushEventRepository>();
            var pushContactRepository = _serviceProvider.GetRequiredService<IPushContactRepository>();

            return webPushSenderSettings.Value.Type switch
            {
                WebPushSenderTypes.Default => new DefaultWebPushSender(
                    webPushSenderSettings,
                    messageQueueSubscriber,
                    loggerFactory.CreateLogger<DefaultWebPushSender>(),
                    webPushEventRepository,
                    pushContactRepository
                ),
                _ => new DefaultWebPushSender(
                    webPushSenderSettings,
                    messageQueueSubscriber,
                    loggerFactory.CreateLogger<DefaultWebPushSender>(),
                    webPushEventRepository,
                    pushContactRepository
                ),
            };
        }
    }

}
