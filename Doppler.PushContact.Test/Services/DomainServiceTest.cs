using AutoFixture;
using Doppler.PushContact.Models.DTOs;
using Doppler.PushContact.Repositories.Interfaces;
using Doppler.PushContact.Services;
using Microsoft.Extensions.Logging;
using Moq;
using System.Threading.Tasks;
using Xunit;

namespace Doppler.PushContact.Test.Services
{
    public class DomainServiceTest
    {
        private static DomainService CreateSut(
            IDomainRepository domainRepository = null,
            IPushContactRepository pushContactRepository = null,
            ILogger<DomainService> logger = null)
        {
            return new DomainService(
                domainRepository ?? Mock.Of<IDomainRepository>(),
                pushContactRepository ?? Mock.Of<IPushContactRepository>(),
                logger ?? Mock.Of<ILogger<DomainService>>()
            );
        }

        [Fact]
        public async Task GetDomainContactStatsAsync_should_return_stats_from_repository()
        {
            // Arrange
            var fixture = new Fixture();
            var domain = fixture.Create<string>();
            var expectedStats = fixture.Create<ContactsStatsDTO>();

            var domainRepositoryMock = new Mock<IDomainRepository>();

            var pushContactRepositoryMock = new Mock<IPushContactRepository>();
            pushContactRepositoryMock
                .Setup(x => x.GetContactsStatsAsync(domain))
                .ReturnsAsync(expectedStats);

            var sut = CreateSut(
                domainRepositoryMock.Object,
                pushContactRepositoryMock.Object
            );

            // Act
            var result = await sut.GetDomainContactStatsAsync(domain);

            // Assert
            Assert.Same(expectedStats, result);
            pushContactRepositoryMock.Verify(x => x.GetContactsStatsAsync(domain), Times.Once);
        }
    }
}
