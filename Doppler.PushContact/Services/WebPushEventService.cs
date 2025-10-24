using Doppler.PushContact.DTOs;
using Doppler.PushContact.Models.Entities;
using Doppler.PushContact.Models.Enums;
using Doppler.PushContact.Repositories.Interfaces;
using Doppler.PushContact.Services.Messages;
using Doppler.PushContact.Transversal;
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
        private readonly IMessageStatsRepository _messageStatsRepository;
        private readonly ILogger<WebPushEventService> _logger;

        public WebPushEventService(
            IWebPushEventRepository webPushEventRepository,
            IPushContactService pushContactService,
            IMessageRepository messageRepository,
            IMessageStatsRepository messageStatsRepository,
            ILogger<WebPushEventService> logger
        )
        {
            _webPushEventRepository = webPushEventRepository;
            _pushContactService = pushContactService;
            _messageRepository = messageRepository;
            _messageStatsRepository = messageStatsRepository;
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
            string eventDescriptor,
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

                // TODO: it isn't allowing register these event types more than once, analize this better.
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

                if (!string.IsNullOrEmpty(eventDescriptor))
                {
                    webPushEvent.EventDescriptor = eventDescriptor;
                }

                await _messageRepository.RegisterEventCount(messageId, webPushEvent);
                await _webPushEventRepository.InsertAsync(webPushEvent, cancellationToken);

                var messageStats = WebPushEventsMapper.MapSingleWebPushEventToMessageStats(webPushEvent);
                await _messageStatsRepository.UpsertMessageStatsAsync(messageStats);
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

        public async Task RegisterWebPushEventsAsync(Guid messageId, IEnumerable<WebPushEvent> webPushEvents, bool registerOnlyFailed)
        {
            if (webPushEvents == null || !webPushEvents.Any())
            {
                return;
            }

            var eventsToRegister = webPushEvents;
            if (registerOnlyFailed)
            {
                eventsToRegister = webPushEvents
                    .Where(e => e.Type == (int)WebPushEventType.DeliveryFailed)
                    .ToList();

                if (eventsToRegister == null || !eventsToRegister.Any())
                {
                    return;
                }
            }

            try
            {
                await _webPushEventRepository.BulkInsertAsync(eventsToRegister);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Unexpected error while registering WebPushEvents for messageId: {messageId}.",
                    messageId
                );
            }
        }
    }
}
