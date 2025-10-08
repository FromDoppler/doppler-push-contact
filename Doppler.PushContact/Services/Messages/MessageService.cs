using Doppler.PushContact.Models.DTOs;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace Doppler.PushContact.Services.Messages
{
    public class MessageService : IMessageService
    {
        private readonly IMessageRepository _messageRepository;
        private readonly ILogger<MessageService> _logger;

        public MessageService(IMessageRepository messageRepository, ILogger<MessageService> logger)
        {
            _messageRepository = messageRepository;
            _logger = logger;
        }

        public async Task<MessageDetails> GetMessageStatsAsync(string domain, Guid messageId, DateTimeOffset dateFrom, DateTimeOffset dateTo)
        {
            return await _messageRepository.GetMessageDetailsAsync(domain, messageId, dateFrom, dateTo);
        }

        public async Task AddMessageAsync(MessageDTO message)
        {
            await _messageRepository.AddAsync(
                message.MessageId,
                message.Domain,
                message.Title,
                message.Body,
                message.OnClickLink,
                0,
                0,
                0,
                message.ImageUrl
            );
        }
    }
}
