using Doppler.PushContact.Models.DTOs;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
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

        public async Task<MessageDTO> GetMessageAsync(Guid messageId)
        {
            var message = await _messageRepository.GetMessageDetailsByMessageIdAsync(messageId);

            if (message != null)
            {
                return MapMessageDTO(message);
            }

            return null;
        }

        public async Task AddMessageAsync(MessageDTO message)
        {
            if (message == null)
            {
                throw new ArgumentNullException(nameof(message));
            }

            if (string.IsNullOrEmpty(message.Domain))
            {
                throw new ArgumentException($"'{nameof(message.Domain)}' cannot be null or empty.", nameof(message.Domain));
            }

            if (string.IsNullOrEmpty(message.Title))
            {
                throw new ArgumentException($"'{nameof(message.Title)}' cannot be null or empty.", nameof(message.Title));
            }

            if (string.IsNullOrEmpty(message.Body))
            {
                throw new ArgumentException($"'{nameof(message.Body)}' cannot be null or empty.", nameof(message.Body));
            }

            ValidateHttpsUrl(message.OnClickLink, nameof(message.OnClickLink));
            ValidateHttpsUrl(message.ImageUrl, nameof(message.ImageUrl));
            ValidateHttpsUrl(message.IconUrl, nameof(message.IconUrl));

            message.Actions = SanitizeActions(message.Actions);
            await _messageRepository.AddAsync(message);
        }

        // TODO: move to Transversal project
        private void ValidateHttpsUrl(string url, string paramName)
        {
            if (string.IsNullOrEmpty(url))
            {
                return;
            }

            if (!Uri.TryCreate(url, UriKind.Absolute, out var result) || result.Scheme != Uri.UriSchemeHttps)
            {
                throw new ArgumentException($"'{paramName}' must be an absolute URL with HTTPS scheme.", paramName);
            }
        }

        private List<MessageActionDTO> SanitizeActions(List<MessageActionDTO> actions)
        {
            var sanitizedActions = new List<MessageActionDTO>();
            if (actions != null)
            {
                var index = 1;
                foreach (var action in actions)
                {
                    var messageActionDto = new MessageActionDTO()
                    {
                        Action = string.IsNullOrEmpty(action.Action) ? $"action{index}" : action.Action,
                        Title = string.IsNullOrEmpty(action.Title) ? $"title{index}" : action.Title,
                        Icon = action.Icon,
                        Link = action.Link,
                    };

                    sanitizedActions.Add(messageActionDto);
                    index++;
                }
            }

            return sanitizedActions;
        }

        private MessageDTO MapMessageDTO(MessageDetails message)
        {
            var messageDto = new MessageDTO()
            {
                MessageId = message.MessageId,
                Title = message.Title,
                Body = message.Body,
                OnClickLink = message.OnClickLink,
                ImageUrl = message.ImageUrl,
                Domain = message.Domain,
                PreferLargeImage = message.PreferLargeImage,
                IconUrl = message.IconUrl,
            };

            var actions = new List<MessageActionDTO>();
            if (message.Actions != null)
            {
                foreach (var action in message.Actions)
                {
                    actions.Add(new MessageActionDTO()
                    {
                        Action = action.Action,
                        Title = action.Title,
                        Icon = action.Icon,
                        Link = action.Link,
                    });
                }
            }

            messageDto.Actions = actions;
            return messageDto;
        }
    }
}
