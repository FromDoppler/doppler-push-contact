using Doppler.PushContact.ApiModels;
using Doppler.PushContact.DopplerSecurity;
using Doppler.PushContact.Models;
using Doppler.PushContact.Models.DTOs;
using Doppler.PushContact.Models.Enums;
using Doppler.PushContact.Models.PushContactApiResponses;
using Doppler.PushContact.Services;
using Doppler.PushContact.Services.Messages;
using Doppler.PushContact.Services.Queue;
using Doppler.PushContact.Transversal;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;

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
        private readonly IWebPushEventService _webPushEventService;
        private readonly ILogger<PushContactController> _logger;
        private readonly IDopplerHttpClient _dopplerHttpClient;

        public PushContactController(IPushContactService pushContactService,
            IMessageSender messageSender,
            IMessageRepository messageRepository,
            IBackgroundQueue backgroundQueue,
            IWebPushEventService webPushEventRepository,
            IDopplerHttpClient dopplerHttpClient,
            ILogger<PushContactController> logger
        )
        {
            _pushContactService = pushContactService;
            _messageSender = messageSender;
            _messageRepository = messageRepository;
            _backgroundQueue = backgroundQueue;
            _webPushEventService = webPushEventRepository;
            _dopplerHttpClient = dopplerHttpClient;
            _logger = logger;
        }

        [AllowAnonymous]
        [HttpPost]
        [Route("push-contacts")]
        public async Task<IActionResult> Add([FromBody] PushContactModel pushContactModel)
        {
            try
            {
                await _pushContactService.AddAsync(pushContactModel);

                // Fire and forget
                _backgroundQueue.QueueBackgroundQueueItem(async (cancellationToken) =>
                {
                    await _dopplerHttpClient.RegisterVisitorSafeAsync(
                        pushContactModel.Domain,
                        pushContactModel.VisitorGuid,
                        pushContactModel.Email
                    );
                });
            }
            catch (ArgumentException argEx)
            {
                return BadRequest(argEx.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Unexpected error adding a new contact with token: {DeviceToken} and subscription: {Subscription}.",
                    pushContactModel.DeviceToken,
                    JsonSerializer.Serialize(pushContactModel.Subscription)
                );
                return StatusCode(StatusCodes.Status500InternalServerError);
            }

            return Ok();
        }

        [AllowAnonymous]
        [HttpPut]
        [Route("/push-contacts/{deviceToken}/subscription")]
        public async Task<IActionResult> UpdateSubscription([FromRoute] string deviceToken, [FromBody] SubscriptionDTO subscription)
        {
            try
            {
                var contactWasUpdated = await _pushContactService.UpdateSubscriptionAsync(deviceToken, subscription);
                if (contactWasUpdated)
                {
                    return Ok();
                }
                else
                {
                    return NotFound("Unexistent 'deviceToken'");
                }
            }
            catch (ArgumentException argEx)
            {
                return BadRequest(argEx.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "An unexpected error occurred updating a contact with token: {DeviceToken} and subscription: {Subscription}.",
                    deviceToken,
                    JsonSerializer.Serialize(subscription)
                );
                return StatusCode((int)HttpStatusCode.InternalServerError);
            }
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
            try
            {
                await _pushContactService.UpdateEmailAsync(deviceToken, email);

                // Fire and forget
                _backgroundQueue.QueueBackgroundQueueItem(async (cancellationToken) =>
                {
                    var visitorInfo = await _pushContactService.GetVisitorInfoSafeAsync(deviceToken);
                    if (visitorInfo != null)
                    {
                        await _dopplerHttpClient.RegisterVisitorSafeAsync(
                            visitorInfo.Domain,
                            visitorInfo.VisitorGuid,
                            visitorInfo.Email
                        );
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Unexpected error updating the email: {ContactEmail} for contact with token: {DeviceToken}.",
                    email,
                    deviceToken
                );
                return StatusCode(StatusCodes.Status500InternalServerError);
            }

            return Ok();
        }

        [Obsolete("This endpoint is not being used. It was replaced by 'messages/domains/{domain}'.")]
        [HttpPost]
        [Route("push-contacts/{domain}/message")]
        public async Task<IActionResult> Message([FromRoute] string domain, [FromBody] Message message)
        {
            var messageId = Guid.NewGuid();

            await _messageRepository.AddAsync(messageId, domain, message.Title, message.Body, message.OnClickLink, 0, 0, 0, message.ImageUrl);

            string pushApiToken = await HttpContext.GetTokenAsync("Bearer", "access_token");

            _backgroundQueue.QueueBackgroundQueueItem(async (cancellationToken) =>
            {
                try
                {
                    var deviceTokens = await _pushContactService.GetAllDeviceTokensByDomainAsync(domain);

                    if (!deviceTokens.Any())
                    {
                        return;
                    }

                    var sendMessageResult = await _messageSender.SendAsync(message.Title, message.Body, deviceTokens, message.OnClickLink, message.ImageUrl, pushApiToken);

                    await _pushContactService.AddHistoryEventsAndMarkDeletedContactsAsync(messageId, sendMessageResult);

                    var sent = sendMessageResult.SendMessageTargetResult.Count();
                    var delivered = sendMessageResult.SendMessageTargetResult.Count(x => x.IsSuccess);
                    var notDelivered = sent - delivered;

                    await _messageRepository.UpdateDeliveriesAsync(messageId, sent, delivered, notDelivered);
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "An unexpected error occurred processing/sending a message with messageId: {messageId}.",
                        messageId
                    );
                }
            });

            return Accepted(new MessageResult()
            {
                MessageId = messageId
            });
        }

        [Obsolete("This endpoint is replaced by 'messages/{messageId}/visitors/{visitorGuid}/send'.")]
        [HttpPost]
        [Route("push-contacts/{domain}/{visitorGuid}/message")]
        public async Task<IActionResult> MessageByVisitorGuid([FromRoute] string domain, [FromRoute] string visitorGuid, [FromBody] Message message)
        {
            var deviceTokens = await _pushContactService.GetAllDeviceTokensByVisitorGuidAsync(visitorGuid);

            var sendMessageResult = await _messageSender.SendAsync(message.Title, message.Body, deviceTokens, message.OnClickLink, message.ImageUrl);

            var messageId = Guid.NewGuid();
            await _pushContactService.AddHistoryEventsAndMarkDeletedContactsAsync(messageId, sendMessageResult);

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
            // obtain web push events summarization
            var webPushEventsSummarization = await _webPushEventService.GetWebPushEventSummarizationAsync(messageId);

            var messageDetailsResponse = new MessageDetailsResponse()
            {
                Domain = domain,
                MessageId = messageId,
                Sent = webPushEventsSummarization.SentQuantity,
                Delivered = webPushEventsSummarization.Delivered,
                NotDelivered = webPushEventsSummarization.NotDelivered,
            };

            // obtain summarized result directly from message
            var messagedetails = await _messageRepository.GetMessageDetailsAsync(domain, messageId);
            if (messagedetails != null && messagedetails.Sent > 0)
            {
                messageDetailsResponse.Sent += messagedetails.Sent;
                messageDetailsResponse.Delivered += messagedetails.Delivered;
                messageDetailsResponse.NotDelivered += messagedetails.NotDelivered;
            }

            return Ok(messageDetailsResponse);

            // TODO: this should be removed, because the summarization should be obtained always from Message.
            //// summarize result from history_events
            //var messageResult = await _pushContactService.GetDeliveredMessageSummarizationAsync(domain, messageId, from, to);
            //return Ok(new MessageDetailsResponse
            //{
            //    Domain = messageResult.Domain,
            //    MessageId = messageId,
            //    Sent = messageResult.SentQuantity + webPushEventsSummarization.SentQuantity,
            //    Delivered = messageResult.Delivered + webPushEventsSummarization.Delivered,
            //    NotDelivered = messageResult.NotDelivered + webPushEventsSummarization.NotDelivered,
            //});
        }

        [HttpGet]
        [Route("push-contacts/{domain}/visitor-guids")]
        public async Task<ActionResult<ApiPage<string>>> GetAllVisitorGuidByDomain([FromRoute] string domain, [FromQuery] int page, [FromQuery] int per_page)
        {
            try
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
            catch (Exception)
            {
                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    new { error = "Unexpected error obtaining visitor-guids." }
                );
            }
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

        [HttpPost]
        [AllowAnonymous]
        [Route("push-contacts/{encryptedContactId}/messages/{encryptedMessageId}/clicked")]
        public IActionResult RegisterWebPushClickedEvent([FromRoute] string encryptedContactId, [FromRoute] string encryptedMessageId)
        {
            return RegisterWebPushEvent(encryptedContactId, encryptedMessageId, WebPushEventType.Clicked);
        }

        [HttpPost]
        [AllowAnonymous]
        [Route("push-contacts/{encryptedContactId}/messages/{encryptedMessageId}/received")]
        public IActionResult RegisterWebPushReceivedEvent([FromRoute] string encryptedContactId, [FromRoute] string encryptedMessageId)
        {
            return RegisterWebPushEvent(encryptedContactId, encryptedMessageId, WebPushEventType.Received);
        }

        private IActionResult RegisterWebPushEvent(string encryptedContactId, string encryptedMessageId, WebPushEventType type)
        {
            string contactId;
            Guid messageIdToGuid;
            try
            {
                contactId = EncryptionHelper.Decrypt(encryptedContactId, useBase64Url: true);

                string messageId = EncryptionHelper.Decrypt(encryptedMessageId, useBase64Url: true);
                messageIdToGuid = Guid.Parse(messageId);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "An unexpected error occurred decrypting contactId: {encryptedContactId} and messageId: {encryptedMessageId}",
                    encryptedContactId,
                    encryptedMessageId
                );
                return BadRequest("Invalid encrypted data.");
            }

            _backgroundQueue.QueueBackgroundQueueItem(async (cancellationToken) =>
            {
                try
                {
                    await _webPushEventService.RegisterWebPushEventAsync(
                        contactId,
                        messageIdToGuid,
                        type,
                        cancellationToken
                    );
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "An unexpected error occurred registering a {webPushEventType} event with messageId: {messageId}.",
                        messageIdToGuid,
                        type
                    );
                }
            });

            return Accepted();
        }
    }
}
