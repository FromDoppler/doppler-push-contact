using Doppler.PushContact.Models.DTOs;
using Doppler.PushContact.Repositories.Interfaces;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;

namespace Doppler.PushContact.Services
{
    public class DomainService : IDomainService
    {
        private readonly IDomainRepository _domainRepository;
        private readonly IPushContactRepository _pushContactRepository;
        private readonly ILogger<DomainService> _logger;

        public DomainService(
            IDomainRepository domainRepository,
            IPushContactRepository pushContactRepository,
            ILogger<DomainService> logger)
        {

            _domainRepository = domainRepository;
            _pushContactRepository = pushContactRepository;
            _logger = logger;
        }

        public async Task UpsertAsync(DomainDTO domain)
        {
            await _domainRepository.UpsertAsync(domain);
        }

        public async Task<DomainDTO> GetByNameAsync(string name)
        {
            var domain = await _domainRepository.GetByNameAsync(name);
            return domain;
        }

        public async Task<ContactsStatsDTO> GetDomainContactStatsAsync(string name)
        {
            var stats = await _pushContactRepository.GetContactsStatsAsync(name);
            return stats;
        }
    }
}
