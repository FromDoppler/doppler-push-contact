using Doppler.PushContact.Models.DTOs;
using Doppler.PushContact.Models.Entities;
using Doppler.PushContact.Models.Enums;
using Doppler.PushContact.Repositories.Interfaces;
using Doppler.PushContact.Transversal;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Doppler.PushContact.Services
{
    public class MessageStatsService : IMessageStatsService
    {
        private readonly IMessageStatsRepository _messageStatsRepository;
        private readonly ILogger<MessageStatsService> _logger;

        public MessageStatsService(
            IMessageStatsRepository repository,
            ILogger<MessageStatsService> logger
        )
        {
            _messageStatsRepository = repository;
            _logger = logger;
        }

        public async Task RegisterMessageStatsAsync(IEnumerable<WebPushEvent> webPushEvents)
        {
            if (webPushEvents == null || !webPushEvents.Any())
            {
                return;
            }

            // group by domain + messageId + truncated hour
            var groupedStats = WebPushEventsHelper.MapWebPushEventsToMessageStatsGroups(webPushEvents);

            try
            {
                await _messageStatsRepository.BulkUpsertStatsAsync(groupedStats);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while registering MessageStats for {Count} groups.", groupedStats.Count);
            }
        }

        public async Task<MessageStatsDTO> GetMessageStatsAsync(
            string domain,
            Guid? messageId,
            DateTimeOffset dateFrom,
            DateTimeOffset dateTo
        )
        {
            try
            {
                return await _messageStatsRepository.GetMessageStatsAsync(domain, messageId, dateFrom, dateTo);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error getting MessageStats for domain: {Domain}, messageId: {messageId}, from: {dateFrom}, to: {dateTo}.",
                    domain,
                    messageId,
                    dateFrom,
                    dateTo
                );

                return new MessageStatsDTO()
                {
                    Domain = domain,
                    MessageId = messageId ?? Guid.Empty,
                    DateFrom = dateFrom,
                    DateTo = dateTo
                };
            }
        }

        public async Task<List<MessageStatsPeriodDTO>> GetMessageStatsByPeriodAsync(
            string domain,
            List<Guid> messageIds,
            DateTimeOffset dateFrom,
            DateTimeOffset dateTo,
            MessageStatsGroupedPeriodEnum periodToGroup
        )
        {
            try
            {
                return await _messageStatsRepository.GetMessageStatsByPeriodAsync(domain, messageIds, dateFrom, dateTo, periodToGroup.ToString().ToLower());
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error getting MessageStats grouped by {periodToGroup} for domain: {Domain}, messageIds: {messageIds}, from: {dateFrom}, to: {dateTo}.",
                    periodToGroup,
                    domain,
                    messageIds,
                    dateFrom,
                    dateTo
                );

                return new List<MessageStatsPeriodDTO>();
            }
        }
    }
}
