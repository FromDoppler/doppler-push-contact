using Doppler.PushContact.Models.DTOs;
using Doppler.PushContact.Models.Entities;
using Doppler.PushContact.Models.Enums;
using Doppler.PushContact.QueuingService.MessageQueueBroker;
using Doppler.PushContact.WebPushSender.DTOs;
using Doppler.PushContact.WebPushSender.Repositories.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using System;
using System.Threading.Tasks;

namespace Doppler.PushContact.WebPushSender.Senders
{
    public class DefaultWebPushSender : WebPushSenderBase
    {
        IPushContactRepository _pushContactRepository;
        IMessageRepository _messageRepository;

        public DefaultWebPushSender(
            IOptions<WebPushSenderSettings> webPushSenderSettings,
            IMessageQueueSubscriber messageQueueSubscriber,
            ILogger<DefaultWebPushSender> logger,
            IWebPushEventRepository weshPushEventRepository,
            IPushContactRepository pushContactRepository,
            IMessageRepository messageRepository
        )
            : base(webPushSenderSettings, messageQueueSubscriber, logger, weshPushEventRepository)
        {
            _pushContactRepository = pushContactRepository;
            _messageRepository = messageRepository;
        }

        public override async Task HandleMessageAsync(DopplerWebPushDTO message)
        {
            _logger.LogDebug(
                "Processing message in \"{QueueName}\":\n\tMessageId: {MessageId}\n\tPushContactId: {PushContactId}",
                _queueName,
                message.MessageId,
                message.PushContactId
            );

            WebPushProcessingResultDTO processingResult = await SendWebPush(message);

            _logger.LogDebug(
                "Message processed:\n\tMessageId: {MessageId}\n\tPushContactId: {PushContactId}\n\tResult: {WebPushProcessingResult}",
                message.MessageId,
                message.PushContactId,
                JsonConvert.SerializeObject(processingResult)
            );

            WebPushEvent webPushEvent = new WebPushEvent()
            {
                Date = DateTime.UtcNow,
                MessageId = message.MessageId,
                PushContactId = message.PushContactId,
                Domain = message.Domain,
            };

            if (processingResult.FailedProcessing)
            {
                webPushEvent.Type = (int)WebPushEventType.ProcessingFailed;
                webPushEvent.SubType = (int)WebPushEventSubType.None;
                // TODO: it must to retry
            }
            else if (processingResult.SuccessfullyDelivered)
            {
                webPushEvent.Type = (int)WebPushEventType.Delivered;
                webPushEvent.SubType = (int)WebPushEventSubType.None;
            }
            else if (processingResult.InvalidSubscription)
            {
                webPushEvent.Type = (int)WebPushEventType.DeliveryFailed;
                webPushEvent.SubType = (int)WebPushEventSubType.InvalidSubcription;
                await _pushContactRepository.MarkDeletedAsync(webPushEvent.PushContactId);
            }
            else if (processingResult.UnknownFail)
            {
                webPushEvent.Type = (int)WebPushEventType.DeliveryFailed;
                webPushEvent.SubType = (int)WebPushEventSubType.UnknownFailure;
                webPushEvent.ErrorMessage = processingResult.ErrorMessage;
            }
            else if (processingResult.LimitsExceeded)
            {
                webPushEvent.Type = (int)WebPushEventType.DeliveryFailedButRetry;
                webPushEvent.SubType = (int)WebPushEventSubType.LimitsExceeded;
                // TODO: it must to retry
            }

            await _weshPushEventRepository.InsertAsync(webPushEvent);
            await _messageRepository.RegisterStatisticsAsync(message.MessageId, webPushEvent);
        }
    }
}
