using Doppler.PushContact.Models.DTOs;
using Doppler.PushContact.QueuingService.MessageQueueBroker;
using Doppler.PushContact.WebPushSender.DTOs;
using Doppler.PushContact.WebPushSender.Repositories.Interfaces;
using Doppler.PushContact.WebPushSender.Senders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Threading.Tasks;

namespace Doppler.PushContact.WebPushSender.Test.Senders.Dummies
{
    // Define delegate
    public delegate Task<WebPushProcessingResultDTO> SendWebPushDelegate(DopplerWebPushDTO message);

    public class TestableDefaultWebPushSender : DefaultWebPushSender
    {
        private readonly SendWebPushDelegate _sendWebPushDelegate;

        public TestableDefaultWebPushSender(
            IOptions<WebPushSenderSettings> webPushSenderSettings,
            IMessageQueueSubscriber messageQueueSubscriber,
            ILogger<DefaultWebPushSender> logger,
            IWebPushEventRepository weshPushEventRepository,
            IPushContactRepository pushContactRepository,
            IMessageRepository messageRepository,
            IMessageStatsRepository messageStatsRepository,
            SendWebPushDelegate sendWebPushDelegate)
            : base(
                webPushSenderSettings,
                messageQueueSubscriber,
                logger,
                weshPushEventRepository,
                pushContactRepository,
                messageRepository,
                messageStatsRepository
            )
        {
            _sendWebPushDelegate = sendWebPushDelegate;
        }

        protected override Task<WebPushProcessingResultDTO> SendWebPush(DopplerWebPushDTO message)
        {
            return _sendWebPushDelegate(message);
        }
    }
}
