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

        public async Task<MessageDetails> GetMessageStatsAsync(string domain, Guid messageId)
        {
            return await _messageRepository.GetMessageDetailsAsync(domain, messageId);
        }
    }
}
