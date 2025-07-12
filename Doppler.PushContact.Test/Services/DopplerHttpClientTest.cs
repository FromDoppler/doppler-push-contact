using Doppler.PushContact.Services;
using Flurl.Http;
using Flurl.Http.Testing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using System;
using System.Threading.Tasks;
using Xunit;

namespace Doppler.PushContact.Test.Services
{
    public class DopplerHttpClientTest
    {
        private static DopplerHttpClient CreateSut(
            IOptions<DopplerHttpClientSettings> dopplerHttpClientSettings = null,
            ILogger<DopplerHttpClient> logger = null
        )
        {
            return new DopplerHttpClient(
                dopplerHttpClientSettings ?? Mock.Of<IOptions<DopplerHttpClientSettings>>(),
                logger ?? Mock.Of<ILogger<DopplerHttpClient>>());
        }

        private DopplerHttpClientSettings GetValidSettings(string url = "https://doppler.example.com")
        {
            return new DopplerHttpClientSettings
            {
                DopplerAppServer = url,
                InternalToken = "test-token"
            };
        }

        [Theory]
        [InlineData(null, "guid", "email@example.com")]
        [InlineData(" ", "guid", "email@example.com")]
        [InlineData("domain", null, "email@example.com")]
        [InlineData("domain", "guid", null)]
        public async Task RegisterVisitorSafeAsync_should_return_false_if_required_fields_are_invalid(string domain, string guid, string email)
        {
            // Arrange
            var sut = CreateSut();

            // Act
            var result = await sut.RegisterVisitorSafeAsync(domain, guid, email);

            // Assert
            Assert.False(result);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public async Task RegisterVisitorSafeAsync_should_return_false_if_settings_are_invalid(string dopplerAppServer)
        {
            // Arrange
            var settings = new DopplerHttpClientSettings
            {
                DopplerAppServer = dopplerAppServer,
                InternalToken = "dummy"
            };

            var optionsMock = Mock.Of<IOptions<DopplerHttpClientSettings>>(o => o.Value == settings);
            var sut = CreateSut(optionsMock);

            // Act
            var result = await sut.RegisterVisitorSafeAsync("domain", "guid", "email@example.com");

            // Assert
            Assert.False(result);
        }

        [Fact]
        public async Task RegisterVisitorSafeAsync_should_return_true_on_successful_response()
        {
            // Arrange
            using var httpTest = new HttpTest();

            httpTest.RespondWith(string.Empty, 200);

            var settings = GetValidSettings();
            var optionsMock = Mock.Of<IOptions<DopplerHttpClientSettings>>(o => o.Value == settings);

            var sut = CreateSut(optionsMock);

            // Act
            var result = await sut.RegisterVisitorSafeAsync("domain", "guid", "email@example.com");

            // Assert
            Assert.True(result);
        }

        [Fact]
        public async Task RegisterVisitorSafeAsync_should_return_false_and_log_if_response_is_not_successful()
        {
            // Arrange
            using var httpTest = new HttpTest();

            httpTest.RespondWith("A Doppler error", 400);

            var loggerMock = new Mock<ILogger<DopplerHttpClient>>();

            var validSettings = GetValidSettings();
            var optionsMock = Mock.Of<IOptions<DopplerHttpClientSettings>>(o => o.Value == validSettings);

            var sut = CreateSut(optionsMock, loggerMock.Object);

            // Act
            var result = await sut.RegisterVisitorSafeAsync("domain", "guid", "email@example.com");

            // Assert
            Assert.False(result);
            loggerMock.Verify(
                x => x.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Doppler contact registration failed")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                Times.Once);
        }

        [Fact]
        public async Task RegisterVisitorSafeAsync_should_return_false_and_log_FlurlHttpException()
        {
            // Arrange
            var loggerMock = new Mock<ILogger<DopplerHttpClient>>();
            var settings = new DopplerHttpClientSettings
            {
                DopplerAppServer = "http://localhost:9999",
                InternalToken = "token"
            };
            var optionsMock = Mock.Of<IOptions<DopplerHttpClientSettings>>(o => o.Value == settings);

            var sut = CreateSut(optionsMock, loggerMock.Object);

            // Act
            var result = await sut.RegisterVisitorSafeAsync("domain", "guid", "email@example.com");

            // Assert
            Assert.False(result);
            loggerMock.Verify(
                x => x.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Unexpected error calling the Doppler endpoint")),
                    It.IsAny<FlurlHttpException>(),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                Times.Once);
        }

        [Fact]
        public async Task RegisterVisitorSafeAsync_should_return_false_and_log_GenericException()
        {
            // Arrange
            var loggerMock = new Mock<ILogger<DopplerHttpClient>>();

            var options = Options.Create(new DopplerHttpClientSettings
            {
                DopplerAppServer = "http://", // invalid URL which throws an generic exception
                InternalToken = "token"
            });

            var sut = CreateSut(options, loggerMock.Object);

            // Act
            var result = await sut.RegisterVisitorSafeAsync("domain", "guid", "email@example.com");

            // Assert
            Assert.False(result);
            loggerMock.Verify(
                x => x.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Unexpected error registering a Doppler contact.")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                Times.Once);
        }
    }
}
