using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using System.Threading.Tasks;
using Doppler.PushContact.Models;
using Doppler.PushContact.DopplerSecurity;
using System;
using Doppler.PushContact.Services;
using System.Linq;
using System.ComponentModel.DataAnnotations;
using Doppler.PushContact.Services.Messages;
using Doppler.PushContact.ApiModels;
using Doppler.PushContact.Services.Queue;
using Microsoft.AspNetCore.Authentication;

namespace Doppler.PushContact.Controllers
{
    [Authorize(Policies.ONLY_SUPERUSER)]
    [ApiController]
    public class PushContactController : ControllerBase
    {
        private readonly IPushContactService _pushContactService;
        private readonly IMessageSender _messageSender;
        private readonly IMessageRepository _messageRepository;
        private readonly IBackgroundQueue _backgroundQueue;

        public PushContactController(IPushContactService pushContactService,
            IMessageSender messageSender,
            IMessageRepository messageRepository,
            IBackgroundQueue backgroundQueue)
        {
            _pushContactService = pushContactService;
            _messageSender = messageSender;
            _messageRepository = messageRepository;
            _backgroundQueue = backgroundQueue;
        }

        [AllowAnonymous]
        [HttpPost]
        [Route("push-contacts")]
        public async Task<IActionResult> Add([FromBody] PushContactModel pushContactModel)
        {
            await _pushContactService.AddAsync(pushContactModel);

            return Ok();
        }

        [HttpGet]
        [Route("push-contacts")]
        public async Task<IActionResult> GetBy([FromQuery, Required] string domain, [FromQuery] string email, [FromQuery] DateTime? modifiedFrom, [FromQuery] DateTime? modifiedTo)
        {
            var pushContactFilter = new PushContactFilter(domain, email, modifiedFrom, modifiedTo);

            var pushContacts = await _pushContactService.GetAsync(pushContactFilter);

            if (pushContacts == null || !pushContacts.Any())
            {
                return NotFound();
            }

            return Ok(pushContacts);
        }

        [HttpPut]
        [Route("/push-contacts/{deviceToken}/visitor-guid")]
        public async Task<IActionResult> UpdatePushContactVisitorGuid([FromRoute] string deviceToken, [FromBody] string visitorGuid)
        {
            if (string.IsNullOrEmpty(deviceToken) || string.IsNullOrWhiteSpace(deviceToken))
            {
                return BadRequest($"'{nameof(deviceToken)}' cannot be null, empty or whitespace.");
            }

            if (string.IsNullOrEmpty(visitorGuid) || string.IsNullOrWhiteSpace(visitorGuid))
            {
                return BadRequest($"'{nameof(visitorGuid)}' cannot be null, empty or whitespace.");
            }

            await _pushContactService.UpdatePushContactVisitorGuid(deviceToken, visitorGuid);
            return Ok();
        }

        [AllowAnonymous]
        [HttpPut]
        [Route("push-contacts/{deviceToken}/email")]
        public async Task<IActionResult> UpdateEmail([FromRoute] string deviceToken, [FromBody] string email)
        {
            await _pushContactService.UpdateEmailAsync(deviceToken, email);

            return Ok();
        }

        [HttpPost]
        [Route("push-contacts/{domain}/message")]
        public async Task<IActionResult> Message([FromRoute] string domain, [FromBody] Message message)
        {
            var messageId = Guid.NewGuid();

            await _messageRepository.AddAsync(messageId, domain, message.Title, message.Body, message.OnClickLink, 0, 0, 0, message.ImageUrl);

            string pushApiToken = await HttpContext.GetTokenAsync("Bearer", "access_token");

            _backgroundQueue.QueueBackgroundQueueItem(async (cancellationToken) =>
            {
                var deviceTokens = await _pushContactService.GetAllDeviceTokensByDomainAsync(domain);

                var sendMessageResult = await _messageSender.SendAsync(message.Title, message.Body, deviceTokens, message.OnClickLink, message.ImageUrl, pushApiToken);

                await _pushContactService.AddHistoryEventsAsync(messageId, sendMessageResult);

                var sent = sendMessageResult.SendMessageTargetResult.Count();
                var delivered = sendMessageResult.SendMessageTargetResult.Count(x => x.IsSuccess);
                var notDelivered = sent - delivered;

                await _messageRepository.UpdateDeliveriesAsync(messageId, sent, delivered, notDelivered);
            });

            return Accepted(new MessageResult()
            {
                MessageId = messageId
            });
        }

        // TODO: move this endpoint to the MessageController
        [HttpPost]
        [Route("push-contacts/{domain}/{visitorGuid}/message")]
        public async Task<IActionResult> MessageByVisitorGuid([FromRoute] string domain, [FromRoute] string visitorGuid, [FromBody] Message message)
        {
            var deviceTokens = await _pushContactService.GetAllDeviceTokensByVisitorGuidAsync(visitorGuid);

            var sendMessageResult = await _messageSender.SendAsync(message.Title, message.Body, deviceTokens, message.OnClickLink, message.ImageUrl);

            var messageId = Guid.NewGuid();
            await _pushContactService.AddHistoryEventsAsync(messageId, sendMessageResult);

            var sent = sendMessageResult.SendMessageTargetResult.Count();
            var delivered = sendMessageResult.SendMessageTargetResult.Count(x => x.IsSuccess);
            var notDelivered = sent - delivered;
            await _messageRepository.AddAsync(messageId, domain, message.Title, message.Body, message.OnClickLink, sent, delivered, notDelivered, message.ImageUrl);

            return Ok(new MessageResult
            {
                MessageId = messageId
            });
        }

        [HttpGet]
        [Route("push-contacts/{domain}/messages/{messageId}/details")]
        public async Task<IActionResult> GetMessageDetails([FromRoute] string domain, [FromRoute] Guid messageId, [FromQuery][Required] DateTimeOffset from, [FromQuery][Required] DateTimeOffset to)
        {
            // obtain summarized result directly from message
            var messagedetails = await _messageRepository.GetMessageDetailsAsync(domain, messageId);
            if (messagedetails != null && messagedetails.Sent > 0)
            {
                return Ok(new
                {
                    messagedetails.Domain,
                    MessageId = messageId,
                    messagedetails.Sent,
                    messagedetails.Delivered,
                    messagedetails.NotDelivered
                });
            }

            // summarize result from history_events
            var messageResult = await _pushContactService.GetDeliveredMessageSummarizationAsync(domain, messageId, from, to);
            return Ok(new
            {
                messageResult.Domain,
                MessageId = messageId,
                Sent = messageResult.SentQuantity,
                messageResult.Delivered,
                messageResult.NotDelivered
            });
        }

        [HttpGet]
        [Route("push-contacts/{domain}/visitor-guids")]
        public async Task<ActionResult<ApiPage<string>>> GetAllVisitorGuidByDomain([FromRoute] string domain, [FromQuery] int page, [FromQuery] int per_page)
        {
            if (string.IsNullOrEmpty(domain) || string.IsNullOrWhiteSpace(domain))
            {
                return BadRequest($"'{nameof(domain)}' cannot be null, empty or whitespace.");
            }
            if (page < 0)
            {
                return BadRequest($"'{nameof(page)}' cannot be lesser than 0.");
            }

            if (per_page <= 0 || per_page > 100)
            {
                return BadRequest($"'{nameof(per_page)}' has to be greater than 0 and lesser than 100.");
            }

            var visitorGuidsList = await _pushContactService.GetAllVisitorGuidByDomain(domain, page, per_page);

            return Ok(visitorGuidsList);
        }

        [AllowAnonymous]
        [HttpGet]
        [Route("push-contacts/{domain}/{visitorGuid}")]
        public async Task<IActionResult> GetEnabledByVisitorGuid([FromRoute] string domain, [FromRoute] string visitorGuid)
        {
            if (string.IsNullOrEmpty(domain) || string.IsNullOrWhiteSpace(domain))
            {
                return BadRequest($"'{nameof(domain)}' cannot be null, empty or whitespace.");
            }

            if (string.IsNullOrEmpty(visitorGuid) || string.IsNullOrWhiteSpace(visitorGuid))
            {
                return BadRequest($"'{nameof(visitorGuid)}' cannot be null, empty or whitespace.");
            }

            var hasPushNotificationEnabled = await _pushContactService.GetEnabledByVisitorGuid(domain, visitorGuid);

            return Ok(hasPushNotificationEnabled);
        }

        [HttpGet]
        [Route("push-contacts/messages/delivery-results")]
        public async Task<ActionResult<ApiPage<MessageDeliveryResult>>> GetMessages([FromQuery] int page, [FromQuery] int per_page, [FromQuery] DateTimeOffset from, [FromQuery] DateTimeOffset to)
        {
            if (from > to)
            {
                return BadRequest($"'{nameof(from)}' cannot be greater than '{nameof(to)}'.");
            }

            if (page < 0)
            {
                return BadRequest($"'{nameof(page)}' cannot be lesser than 0.");
            }

            if (per_page <= 0 || per_page > 100)
            {
                return BadRequest($"'{nameof(per_page)}' has to be greater than 0 and lesser than 100.");
            }

            var apiPage = await _messageRepository.GetMessages(page, per_page, from, to);
            return Ok(apiPage);
        }

        [HttpGet]
        [Route("push-contacts/domains")]
        public async Task<ActionResult<ApiPage<DomainInfo>>> GetDomains(int page, int per_page)
        {
            const int limit = 100;

            if (page < 0)
            {
                return BadRequest($"'{nameof(page)}' cannot be lesser than 0.");
            }

            if (per_page <= 0 || per_page > limit)
            {
                return BadRequest($"'{nameof(per_page)}' has to be greater than 0 and lesser than ${limit}.");
            }

            var apiPage = await _pushContactService.GetDomains(page, per_page);
            return Ok(apiPage);
        }
    }
}
