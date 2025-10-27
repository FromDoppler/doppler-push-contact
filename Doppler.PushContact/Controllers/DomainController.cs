using Doppler.PushContact.DopplerSecurity;
using Doppler.PushContact.Models;
using Doppler.PushContact.Models.DTOs;
using Doppler.PushContact.Models.Models;
using Doppler.PushContact.Models.PushContactApiResponses;
using Doppler.PushContact.Services;
using Doppler.PushContact.Services.Messages;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;

namespace Doppler.PushContact.Controllers
{
    [Authorize(Policies.ONLY_SUPERUSER)]
    [ApiController]
    public class DomainController : ControllerBase
    {
        private readonly IDomainService _domainService;
        private readonly IMessageService _messageService;
        private readonly IMessageStatsService _messageStatsService;
        private readonly ILogger<DomainController> _logger;

        public DomainController(
            IDomainService domainService,
            IWebPushEventService webPushEventService,
            IMessageService messageService,
            IMessageStatsService messageStatsService,
            ILogger<DomainController> logger
        )
        {
            _domainService = domainService;
            _messageService = messageService;
            _messageStatsService = messageStatsService;
            _logger = logger;
        }

        // TODO: analyze separating into two methods (PUT/POST) because using PUT, and not all fields may be provided,
        // which could result in unintended deletions or resets.
        [HttpPut]
        [Route("domains/{name}")]
        public async Task<IActionResult> Upsert([FromRoute] string name, [FromBody] Domain domain)
        {
            var domainDTO = new DomainDTO()
            {
                Name = name,
                IsPushFeatureEnabled = domain.IsPushFeatureEnabled,
                UsesExternalPushDomain = domain.UsesExternalPushDomain,
                ExternalPushDomain = domain.ExternalPushDomain,
            };
            await _domainService.UpsertAsync(domainDTO);

            return Ok();
        }

        [AllowAnonymous]
        [HttpGet]
        [Route("domains/{name}/isPushFeatureEnabled")]
        [ResponseCache(Location = ResponseCacheLocation.Any, Duration = 120)]
        public async Task<ActionResult<bool>> GetPushFeatureStatus([FromRoute] string name)
        {
            try
            {
                var domain = await _domainService.GetByNameAsync(name);

                if (domain == null)
                {
                    return NotFound();
                }

                return domain.IsPushFeatureEnabled;
            }
            catch (Exception ex)
            {
                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    new { error = "An unexpected error occurred obtaining domain information." }
                );
            }
        }

        [HttpGet]
        [Route("domains/{name}")]
        public async Task<ActionResult<Domain>> GetDomain([FromRoute] string name)
        {
            try
            {
                var domain = await _domainService.GetByNameAsync(name);

                if (domain == null)
                {
                    return NotFound();
                }

                return new Domain()
                {
                    Name = domain.Name,
                    IsPushFeatureEnabled = domain.IsPushFeatureEnabled,
                    UsesExternalPushDomain = domain.UsesExternalPushDomain,
                    ExternalPushDomain = domain.ExternalPushDomain,
                };
            }
            catch (Exception ex)
            {
                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    new { error = "An unexpected error occurred obtaining domain information." }
                );
            }
        }

        [HttpGet]
        [Route("domains/{name}/stats")]
        public async Task<ActionResult<DomainStats>> GetDomainStats([FromRoute] string name)
        {
            try
            {
                var domain = await _domainService.GetByNameAsync(name);

                if (domain == null)
                {
                    return NotFound();
                }

                var contactStats = await _domainService.GetDomainContactStatsAsync(domain.Name);

                var domainStats = new DomainStats()
                {
                    Name = contactStats.DomainName,
                    ContactsStats = new ContactsStats()
                    {
                        Active = contactStats.Active,
                        Deleted = contactStats.Deleted,
                        Total = contactStats.Total,
                    }
                };

                return Ok(domainStats);
            }
            catch (Exception ex)
            {
                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    new { error = "An unexpected error occurred obtaining domain stats." }
                );
            }
        }

        [AllowAnonymous]
        [HttpGet]
        [Route("domains/{name}/push-configuration")]
        public async Task<ActionResult<DomainPushConfiguration>> GetPushConfiguration([FromRoute] string name)
        {
            try
            {
                var domain = await _domainService.GetByNameAsync(name);

                if (domain == null)
                {
                    return NotFound();
                }

                return new DomainPushConfiguration()
                {
                    IsPushFeatureEnabled = domain.IsPushFeatureEnabled,
                    UsesExternalPushDomain = domain.UsesExternalPushDomain,
                    ExternalPushDomain = domain.ExternalPushDomain,
                };
            }
            catch (Exception ex)
            {
                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    new { error = "An unexpected error occurred obtaining domain push configuration." }
                );
            }
        }

        [HttpGet]
        [Route("domains/{domain}/push-sends-consumed-count")]
        [ResponseCache(Location = ResponseCacheLocation.Any, Duration = 60)]
        public async Task<IActionResult> GetConsumedSends([FromRoute] string domain, [FromQuery][Required] DateTimeOffset from, [FromQuery][Required] DateTimeOffset to)
        {
            try
            {
                var messageStats = await _messageStatsService.GetMessageStatsAsync(domain, null, from, to);

                var response = new PushSendsConsumedResponse
                {
                    Domain = domain,
                    From = from,
                    To = to,
                    Consumed = messageStats.BillableSends,
                };

                return Ok(response);
            }
            catch (Exception)
            {
                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    new { error = "Unexpected error obtaining consumed. Try again." }
                );
            }
        }

        [HttpGet]
        [Route("domains/{domain}/messages/{messageId}/stats")]
        public async Task<ActionResult<MessageDetailsResponse>> GetMessageStats([FromRoute] string domain, [FromRoute] Guid messageId, [FromQuery][Required] DateTimeOffset from, [FromQuery][Required] DateTimeOffset to)
        {
            var response = new MessageDetailsResponse()
            {
                Domain = domain,
                MessageId = messageId,
            };

            try
            {
                // obtain stats from MessageStats
                var messageStats = await _messageStatsService.GetMessageStatsAsync(domain, messageId, from, to);
                if (messageStats != null && messageStats.Sent > 0)
                {
                    response.Sent = messageStats.Sent;
                    response.Delivered = messageStats.Delivered;
                    response.NotDelivered = messageStats.NotDelivered;
                    response.BillableSends = messageStats.BillableSends;
                    response.Clicks = messageStats.Click;
                    response.Received = messageStats.Received;
                    response.ActionClick = messageStats.ActionClick;
                }
                else
                {
                    // obtain stats from Messages
                    var statsObtainedFromMessage = await _messageService.GetMessageStatsAsync(domain, messageId, from, to);

                    if (statsObtainedFromMessage != null)
                    {
                        response.Sent = statsObtainedFromMessage.Sent;
                        response.Delivered = statsObtainedFromMessage.Delivered;
                        response.NotDelivered = statsObtainedFromMessage.NotDelivered;
                        response.BillableSends = statsObtainedFromMessage.BillableSends;
                        response.Clicks = statsObtainedFromMessage.Clicks;
                        response.Received = statsObtainedFromMessage.Received;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "An unexpected error occurred obtaining message stats. Domain: {domain} and messageId: {messageId}",
                    domain,
                    messageId
                );
            }

            return Ok(response);
        }
    }
}
