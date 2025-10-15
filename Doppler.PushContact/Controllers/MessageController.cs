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
        private readonly IMessageService _messageService;
        private readonly ILogger<MessageController> _logger;

        public MessageController(
            IPushContactService pushContactService,
            IMessageRepository messageRepository,
            IMessageSender messageSender,
            IWebPushPublisherService webPushPublisherService,
            IMessageService messageService,
            ILogger<MessageController> logger
        )
        {
            _pushContactService = pushContactService;
            _messageRepository = messageRepository;
            _messageSender = messageSender;
            _webPushPublisherService = webPushPublisherService;
            _messageService = messageService;
            _logger = logger;
        }

        [Obsolete("This endpoint is deprecated and will be replaced by 'messages/{messageId}/visitors/{visitorGuid}/send'.")]
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
            var sendMessageResult = await _messageSender.SendAsync(message.Title, message.Body, deviceTokens, message.OnClickLink, message.ImageUrl);

            await _pushContactService.MarkDeletedContactsAsync(messageId, sendMessageResult);

            var sent = sendMessageResult.SendMessageTargetResult.Count();
            var delivered = sendMessageResult.SendMessageTargetResult.Count(x => x.IsSuccess);
            await _messageRepository.IncrementMessageStats(messageId, sent, delivered, sent - delivered);

            return Ok(new MessageResult
            {
                MessageId = messageId
            });
        }

        [HttpPost]
        [Route("messages/{messageId}/visitors/{visitorGuid}/send")]
        public async Task<IActionResult> ProcessWebPushForVisitorGuid(
            [FromRoute] Guid messageId,
            [FromRoute] string visitorGuid,
            [FromBody] FieldsReplacement fieldsReplacement
        )
        {
            try
            {
                var message = await _messageService.GetMessageAsync(messageId);
                if (message == null)
                {
                    return NotFound($"A Message with messageId: {messageId} doesn't exist.");
                }

                var missingFieldsInTitle = GetMissingReplacements(message.Title, fieldsReplacement.Fields);
                var missingFieldsInBody = GetMissingReplacements(message.Body, fieldsReplacement.Fields);
                if (fieldsReplacement.ReplacementIsMandatory && (missingFieldsInTitle.Count > 0 || missingFieldsInBody.Count > 0))
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
                    Title = message.Title,
                    Body = message.Body,
                    OnClickLink = message.OnClickLink,
                    ImageUrl = message.ImageUrl,
                    Domain = message.Domain,
                    Actions = message.Actions,
                };

                var visitorsWithReplacements = new FieldsReplacementList()
                {
                    ReplacementIsMandatory = fieldsReplacement.ReplacementIsMandatory,
                    VisitorsFieldsList = new List<VisitorFields>()
                    {
                        new VisitorFields{
                            VisitorGuid = visitorGuid,
                            Fields = fieldsReplacement.Fields,
                        },
                    },
                };

                var authenticationApiToken = await HttpContext.GetTokenAsync("Bearer", "access_token");

                _webPushPublisherService.ProcessWebPushForVisitors(webPushDTO, visitorsWithReplacements, authenticationApiToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "An unexpected error occurred processing web push for MessageId: {MessageId} and visitorGuid: {VisitorGuid}",
                    messageId,
                    visitorGuid
                );
                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    new { error = "Unexpected error processing web push." }
                );
            }

            return Accepted(new MessageResult()
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

        [HttpPost]
        [Route("messages/{messageId}/visitors/send")]
        public async Task<IActionResult> ProcessWebPushForVisitors(
            [FromRoute] Guid messageId,
            [FromBody] FieldsReplacementList visitorsWithReplacements
        )
        {
            try
            {
                var message = await _messageService.GetMessageAsync(messageId);
                if (message == null)
                {
                    return NotFound($"A Message with messageId: {messageId} doesn't exist.");
                }

                if (visitorsWithReplacements == null || visitorsWithReplacements.VisitorsFieldsList == null || visitorsWithReplacements.VisitorsFieldsList.Count == 0)
                {
                    return BadRequest(new
                    {
                        error = "There are not visitor guids to be processed.",
                    });
                }

                var webPushDTO = new WebPushDTO()
                {
                    MessageId = messageId,
                    Title = message.Title,
                    Body = message.Body,
                    OnClickLink = message.OnClickLink,
                    ImageUrl = message.ImageUrl,
                    Domain = message.Domain,
                    Actions = message.Actions,
                };

                var authenticationApiToken = await HttpContext.GetTokenAsync("Bearer", "access_token");

                _webPushPublisherService.ProcessWebPushForVisitors(webPushDTO, visitorsWithReplacements, authenticationApiToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "An unexpected error occurred processing web push for MessageId: {MessageId}.",
                    messageId
                );
                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    new { error = "Unexpected error processing web push." }
                );
            }

            return Accepted(new MessageResult()
            {
                MessageId = messageId
            });
        }

        [HttpPost]
        [Route("message")]
        public async Task<IActionResult> CreateMessage([FromBody] MessageBody messageBody)
        {
            try
            {
                var messageDto = new MessageDTO()
                {
                    MessageId = Guid.NewGuid(),
                    Domain = messageBody.Domain,
                    Title = messageBody.Message.Title,
                    Body = messageBody.Message.Body,
                    OnClickLink = messageBody.Message.OnClickLink,
                    ImageUrl = messageBody.Message.ImageUrl,
                    Actions = MapActions(messageBody.Message.Actions),
                };
                await _messageService.AddMessageAsync(messageDto);

                return Ok(new MessageResult
                {
                    MessageId = messageDto.MessageId,
                });
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
                    messageBody.Domain
                );

                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    new { error = $"An unexpected error occurred: {ex.Message}" }
                );
            }
        }

        [HttpPost]
        [Route("messages/domains/{domain}")]
        public async Task<IActionResult> ProcessWebPushByDomain([FromRoute] string domain, [FromBody] Message message)
        {
            var messageId = Guid.NewGuid();
            try
            {
                var messageDto = new MessageDTO()
                {
                    MessageId = messageId,
                    Domain = domain,
                    Title = message.Title,
                    Body = message.Body,
                    OnClickLink = message.OnClickLink,
                    ImageUrl = message.ImageUrl,
                    Actions = MapActions(message.Actions),
                };
                await _messageService.AddMessageAsync(messageDto);
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

            // TODO: it obtains the message previously created. Maybe when AddMessageAsync should return the message instead messageId.
            var createdMessage = await _messageService.GetMessageAsync(messageId);
            var webPushDTO = new WebPushDTO()
            {
                MessageId = messageId,
                Title = createdMessage.Title,
                Body = createdMessage.Body,
                OnClickLink = createdMessage.OnClickLink,
                ImageUrl = createdMessage.ImageUrl,
                Domain = domain,
                Actions = createdMessage.Actions,
            };

            var authenticationApiToken = await HttpContext.GetTokenAsync("Bearer", "access_token");

            _webPushPublisherService.ProcessWebPushByDomainInBatches(domain, webPushDTO, authenticationApiToken);

            return Accepted(new MessageResult()
            {
                MessageId = messageId
            });
        }

        private List<MessageActionDTO> MapActions(List<MessageAction> actions)
        {
            var result = new List<MessageActionDTO>();
            if (actions == null)
            {
                return result;
            }

            foreach (var action in actions)
            {
                var dto = new MessageActionDTO()
                {
                    Action = action.Action,
                    Title = action.Title,
                    Icon = action.Icon,
                    Link = action.Link,
                };

                result.Add(dto);
            }

            return result;
        }
    }
}
