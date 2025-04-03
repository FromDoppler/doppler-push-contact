using Doppler.PushContact.DopplerSecurity;
using Doppler.PushContact.Models;
using Doppler.PushContact.Services;
using Doppler.PushContact.Services.Messages;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading.Tasks;

namespace Doppler.PushContact.Controllers
{
    [Authorize(Policies.ONLY_SUPERUSER)]
    [ApiController]
    public class DomainController : ControllerBase
    {
        private readonly IDomainService _domainService;
        private readonly IMessageRepository _messageRepository;
        private readonly IMessageSender _messageSender;

        public DomainController(IDomainService domainService, IMessageRepository messageRepository, IMessageSender messageSender)
        {
            _domainService = domainService;
            _messageRepository = messageRepository;
            _messageSender = messageSender;
        }

        // TODO: analyze separating into two methods (PUT/POST) because using PUT, and not all fields may be provided,
        // which could result in unintended deletions or resets.
        [HttpPut]
        [Route("domains/{name}")]
        public async Task<IActionResult> Upsert([FromRoute] string name, [FromBody] Domain domain)
        {
            domain.Name = name;
            await _domainService.UpsertAsync(domain);

            return Ok();
        }

        [AllowAnonymous]
        [HttpGet]
        [Route("domains/{name}/isPushFeatureEnabled")]
        [ResponseCache(Location = ResponseCacheLocation.Any, Duration = 120)]
        public async Task<ActionResult<bool>> GetPushFeatureStatus([FromRoute] string name)
        {
            var domain = await _domainService.GetByNameAsync(name);

            if (domain == null)
            {
                return NotFound();
            }

            return domain.IsPushFeatureEnabled;
        }
    }
}
