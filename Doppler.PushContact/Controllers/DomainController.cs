using Doppler.PushContact.DopplerSecurity;
using Doppler.PushContact.Models;
using Doppler.PushContact.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
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

        public DomainController(IDomainService domainService)
        {
            _domainService = domainService;
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

                return domain;
            }
            catch (Exception ex)
            {
                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    new { error = "An unexpected error occurred obtaining domain information." }
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
    }
}
