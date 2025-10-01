using Doppler.PushContact.DTOs;
using Doppler.PushContact.Models.Entities;
using Doppler.PushContact.Models.Enums;
using Doppler.PushContact.Repositories.Interfaces;
using Doppler.PushContact.Services.Messages;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Doppler.PushContact.Services
{
    public class WebPushEventService : IWebPushEventService
    {
        private readonly IWebPushEventRepository _webPushEventRepository;
        private readonly IPushContactService _pushContactService;
        private readonly IMessageRepository _messageRepository;
        private readonly ILogger<WebPushEventService> _logger;

        public WebPushEventService(
            IWebPushEventRepository webPushEventRepository,
            IPushContactService pushContactService,
            IMessageRepository messageRepository,
            ILogger<WebPushEventService> logger
        )
        {
            _webPushEventRepository = webPushEventRepository;
            _pushContactService = pushContactService;
            _messageRepository = messageRepository;
            _logger = logger;
        }

        public async Task<WebPushEventSummarizationDTO> GetWebPushEventSummarizationAsync(Guid messageId)
        {
            try
            {
                return await _webPushEventRepository.GetWebPushEventSummarization(messageId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error summarizing 'WebPushEvents' with {nameof(messageId)} {messageId}");
                return new WebPushEventSummarizationDTO()
                {
                    MessageId = messageId,
                    SentQuantity = 0,
                    Delivered = 0,
                    NotDelivered = 0,
                };
            }
        }

        public async Task<bool> RegisterWebPushEventAsync(
            string contactId,
            Guid messageId,
            WebPushEventType type,
            CancellationToken cancellationToken
        )
        {
            try
            {
                var contactDomain = await _pushContactService.GetPushContactDomainAsync(contactId);
                var messageDomain = await _messageRepository.GetMessageDomainAsync(messageId);

                if (contactDomain == null || messageDomain == null || contactDomain.ToLower() != messageDomain.ToLower())
                {
                    return false;
                }

                if (await _webPushEventRepository.IsWebPushEventRegistered(contactId, messageId, type))
                {
                    return false;
                }

                WebPushEvent webPushEvent = new WebPushEvent()
                {
                    Date = DateTime.UtcNow,
                    MessageId = messageId,
                    PushContactId = contactId,
                    Type = (int)type,
                    Domain = contactDomain,
                };

                await _messageRepository.RegisterEventCount(messageId, webPushEvent);
                await _webPushEventRepository.InsertAsync(webPushEvent, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Unexpected error while registering WebPushEvent for contactId: {contactId}, messageId: {messageId}, eventType: {type}",
                    contactId,
                    messageId,
                    type.ToString()
                );
                return false;
            }

            return true;
        }

        public async Task<int> GetWebPushEventConsumed(string domain, DateTimeOffset dateFrom, DateTimeOffset dateTo)
        {
            return await _webPushEventRepository.GetWebPushEventConsumed(domain, dateFrom, dateTo);
        }

        public async Task<IEnumerable<WebPushEvent>> RegisterWebPushEventsAsync(string domain, Guid messageId, SendMessageResult sendMessageResult)
        {
            if (sendMessageResult == null)
            {
                return null;
            }

            var webPushEvents = sendMessageResult.SendMessageTargetResult?
                .Select(sendResult => new WebPushEvent
                {
                    Domain = domain,
                    MessageId = messageId,
                    DeviceToken = sendResult.TargetDeviceToken,
                    Date = DateTime.UtcNow,
                    Type = sendResult.IsSuccess ? (int)WebPushEventType.Delivered : (int)WebPushEventType.DeliveryFailed,
                    SubType = sendResult.IsSuccess ? (int)WebPushEventSubType.None :
                            sendResult.IsValidTargetDeviceToken ? (int)WebPushEventSubType.UnknownFailure : (int)WebPushEventSubType.InvalidSubcription,
                    ErrorMessage = sendResult.NotSuccessErrorDetails,
                }
                );

            if (webPushEvents != null && webPushEvents.Any())
            {
                try
                {
                    await _webPushEventRepository.BulkInsertAsync(webPushEvents);
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Unexpected error while registering WebPushEvents for domain: {domain}, messageId: {messageId}.",
                        domain,
                        messageId
                    );
                }

                return webPushEvents;
            }

            return null;
        }
    }
}
