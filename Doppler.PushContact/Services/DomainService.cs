using Doppler.PushContact.Models;
using Doppler.PushContact.Repositories.Interfaces;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;

namespace Doppler.PushContact.Services
{
    public class DomainService : IDomainService
    {
        private readonly IDomainRepository _domainRepository;
        private readonly ILogger<DomainService> _logger;

        public DomainService(
            IDomainRepository domainRepository,
            ILogger<DomainService> logger)
        {

            _domainRepository = domainRepository;
            _logger = logger;
        }

        public async Task UpsertAsync(Domain domain)
        {
            await _domainRepository.UpsertAsync(domain);
        }

        public async Task<Domain> GetByNameAsync(string name)
        {
            var domain = await _domainRepository.GetByNameAsync(name);
            return domain;
        }
    }
}
