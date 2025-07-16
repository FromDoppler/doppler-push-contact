using Doppler.PushContact.DopplerSecurity;
using Doppler.PushContact.Models;
using Doppler.PushContact.Models.DTOs;
using Doppler.PushContact.Models.Models;
using Doppler.PushContact.Services;
using Doppler.PushContact.Services.Messages;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Doppler.PushContact.Controllers
{
    [Authorize(Policies.ONLY_SUPERUSER)]
    [ApiController]
    public class MessageController : ControllerBase
    {
        private readonly IMessageSender _messageSender;
        private readonly IMessageRepository _messageRepository;
        private readonly IPushContactService _pushContactService;
        private readonly IWebPushPublisherService _webPushPublisherService;
        private readonly ILogger<MessageController> _logger;

        public MessageController(
            IPushContactService pushContactService,
            IMessageRepository messageRepository,
            IMessageSender messageSender,
            IWebPushPublisherService webPushPublisherService,
            ILogger<MessageController> logger
        )
        {
            _pushContactService = pushContactService;
            _messageRepository = messageRepository;
            _messageSender = messageSender;
            _webPushPublisherService = webPushPublisherService;
            _logger = logger;
        }

        [Obsolete("This endpoint is deprecated and will be replaced by 'messages/{messageId}/visitors/{visitorGuid}'.")]
        [HttpPost]
        [Route("message/{messageId}")]
        public async Task<IActionResult> MessageByVisitorGuid([FromRoute] Guid messageId, [FromBody] string visitorGuid)
        {
            if (string.IsNullOrWhiteSpace(visitorGuid))
            {
                return BadRequest($"'{nameof(visitorGuid)}' cannot be null, empty or whitespace.");
            }
            if (string.IsNullOrWhiteSpace(messageId.ToString()))
            {
                return BadRequest($"'{nameof(messageId)}' cannot be null, empty or whitespace.");
            }

            var deviceTokens = await _pushContactService.GetAllDeviceTokensByVisitorGuidAsync(visitorGuid);
            var message = await _messageRepository.GetMessageDetailsByMessageIdAsync(messageId);
            var sendMessageResult = await _messageSender.SendAsync(message.Title, message.Body, deviceTokens, message.OnClickLinkPropName, message.ImageUrl);

            await _pushContactService.AddHistoryEventsAsync(messageId, sendMessageResult);

            var sent = sendMessageResult.SendMessageTargetResult.Count();
            var delivered = sendMessageResult.SendMessageTargetResult.Count(x => x.IsSuccess);
            await _messageRepository.IncrementMessageStats(messageId, sent, delivered, sent - delivered);

            return Ok(new MessageResult
            {
                MessageId = messageId
            });
        }

        [HttpPost]
        [Route("messages/{messageId}/visitors/{visitorGuid}")]
        public async Task<IActionResult> EnqueueWebPushForVisitorGuid(
            [FromRoute] Guid messageId,
            [FromRoute] string visitorGuid,
            [FromBody] MessageReplacements messageReplacements
        )
        {
            if (string.IsNullOrWhiteSpace(visitorGuid))
            {
                return BadRequest("'visitorGuid' cannot be empty.");
            }

            try
            {
                var message = await _messageRepository.GetMessageDetailsByMessageIdAsync(messageId);
                if (message == null)
                {
                    return NotFound($"A Message with messageId: {messageId} doesn't exist.");
                }

                var missingFieldsInTitle = GetMissingReplacements(message.Title, messageReplacements.Values);
                var missingFieldsInBody = GetMissingReplacements(message.Body, messageReplacements.Values);
                if (messageReplacements.ReplacementIsMandatory && (missingFieldsInTitle.Count > 0 || missingFieldsInBody.Count > 0))
                {
                    return BadRequest(new
                    {
                        error = "Missing replacements values in title or body.",
                        missingFieldsInTitle,
                        missingFieldsInBody,
                    });
                }

                var webPushDTO = new WebPushDTO()
                {
                    MessageId = messageId,
                    Title = ReplaceFields(message.Title, messageReplacements.Values),
                    Body = ReplaceFields(message.Body, messageReplacements.Values),
                    OnClickLink = message.OnClickLinkPropName,
                    ImageUrl = message.ImageUrl
                };

                var authenticationApiToken = await HttpContext.GetTokenAsync("Bearer", "access_token");

                _webPushPublisherService.ProcessWebPushForVisitor(visitorGuid, webPushDTO, authenticationApiToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "An unexpected error occurred sending web push. MessageId: {MessageId} and visitorGuid: {VisitorGuid}",
                    messageId,
                    visitorGuid
                );
                return BadRequest("Unexpected error sending web push.");
            }

            return Ok(new MessageResult
            {
                MessageId = messageId
            });
        }

        private List<string> GetMissingReplacements(string content, Dictionary<string, string> values)
        {
            var lowerKeys = (values ?? new Dictionary<string, string>())
                .Keys.Select(k => k.ToLowerInvariant()).ToHashSet();

            var missing = new HashSet<string>();

            var matches = Regex.Matches(content, @"\[\[\[([\w\.\-]+)\]\]\]");
            foreach (Match match in matches)
            {
                var key = match.Groups[1].Value;
                if (!lowerKeys.Contains(key.ToLowerInvariant()))
                {
                    missing.Add(key);
                }
            }

            return missing.ToList();
        }

        public static string ReplaceFields(string template, Dictionary<string, string> values)
        {
            if (values == null)
            {
                return template;
            }

            var lowerValues = values.ToDictionary(k => k.Key.ToLowerInvariant(), v => v.Value);

            return Regex.Replace(template, @"\[\[\[([\w\.\-]+)\]\]\]", match =>
            {
                var key = match.Groups[1].Value;
                var lowerKey = key.ToLowerInvariant();
                return lowerValues.TryGetValue(lowerKey, out var value) ? value ?? string.Empty : match.Value;
            });
        }

        [HttpPost]
        [Route("message")]
        public async Task<IActionResult> CreateMessage([FromBody] MessageBody messageBody)
        {
            try
            {
                // TODO: analyze remotion of validation for the title and body, it's being doing during model binding with annotations.
                _messageSender.ValidateMessage(messageBody.Message.Title, messageBody.Message.Body, messageBody.Message.OnClickLink, messageBody.Message.ImageUrl);
            }
            catch (ArgumentException argExc)
            {
                return UnprocessableEntity(argExc.Message);
            }

            var messageId = Guid.NewGuid();

            await _messageRepository.AddAsync(
                messageId, messageBody.Domain,
                messageBody.Message.Title,
                messageBody.Message.Body,
                messageBody.Message.OnClickLink,
                0,
                0,
                0,
                messageBody.Message.ImageUrl
            );

            return Ok(new MessageResult
            {
                MessageId = messageId
            });
        }

        [HttpPost]
        [Route("messages/domains/{domain}")]
        public async Task<IActionResult> EnqueueWebPush([FromRoute] string domain, [FromBody] Message message)
        {
            Guid messageId;
            try
            {
                messageId = await _messageSender.AddMessageAsync(domain, message.Title, message.Body, message.OnClickLink, message.ImageUrl);
            }
            catch (ArgumentException argEx)
            {
                return BadRequest(new { error = argEx.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "An unexpected error occurred adding a message for domain: {domain}.",
                    domain
                );

                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    new { error = $"An unexpected error occurred: {ex.Message}" }
                );
            }

            var webPushDTO = new WebPushDTO()
            {
                Title = message.Title,
                Body = message.Body,
                OnClickLink = message.OnClickLink,
                ImageUrl = message.ImageUrl,
                MessageId = messageId,
            };

            var authenticationApiToken = await HttpContext.GetTokenAsync("Bearer", "access_token");

            _webPushPublisherService.ProcessWebPushInBatches(domain, webPushDTO, authenticationApiToken);

            return Accepted(new MessageResult()
            {
                MessageId = messageId
            });
        }
    }
}
