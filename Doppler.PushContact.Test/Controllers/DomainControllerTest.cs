using AutoFixture;
using Doppler.PushContact.Models;
using Doppler.PushContact.Services;
using Doppler.PushContact.Services.Messages;
using Doppler.PushContact.Test.Controllers.Utils;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Doppler.PushContact.Test.Controllers
{
    public class DomainControllerTest : IClassFixture<WebApplicationFactory<Startup>>
    {
        private readonly WebApplicationFactory<Startup> _factory;
        private readonly ITestOutputHelper _output;

        public DomainControllerTest(WebApplicationFactory<Startup> factory, ITestOutputHelper output)
        {
            _factory = factory;
            _output = output;
        }

        [Theory]
        [InlineData(TestApiUsersData.TOKEN_EMPTY)]
        [InlineData(TestApiUsersData.TOKEN_BROKEN)]
        public async Task Upsert_should_return_unauthorized_when_token_is_not_valid(string token)
        {
            // Arrange
            var client = _factory.CreateClient(new WebApplicationFactoryClientOptions());

            var fixture = new Fixture();
            var name = fixture.Create<string>();

            var request = new HttpRequestMessage(HttpMethod.Put, $"domains/{name}")
            {
                Headers = { { "Authorization", $"Bearer {token}" } }
            };

            // Act
            var response = await client.SendAsync(request);
            _output.WriteLine(response.GetHeadersAsString());

            // Assert
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Theory]
        [InlineData(TestApiUsersData.TOKEN_SUPERUSER_EXPIRE_20010908)]
        public async Task Upsert_should_return_unauthorized_when_token_is_a_expired_superuser_token(string token)
        {
            // Arrange
            var client = _factory.CreateClient(new WebApplicationFactoryClientOptions());

            var fixture = new Fixture();
            var name = fixture.Create<string>();

            var request = new HttpRequestMessage(HttpMethod.Put, $"domains/{name}")
            {
                Headers = { { "Authorization", $"Bearer {token}" } }
            };

            // Act
            var response = await client.SendAsync(request);
            _output.WriteLine(response.GetHeadersAsString());

            // Assert
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Theory]
        [InlineData(TestApiUsersData.TOKEN_SUPERUSER_NOTDEFINED_EXPIRE_20330518)]
        [InlineData(TestApiUsersData.TOKEN_SUPERUSER_FALSE_EXPIRE_20330518)]
        [InlineData(TestApiUsersData.TOKEN_ACCOUNT_123_TEST1_AT_TEST_DOT_COM_EXPIRE_20330518)]
        public async Task Upsert_should_require_a_valid_token_with_isSU_flag(string token)
        {
            // Arrange
            var client = _factory.CreateClient(new WebApplicationFactoryClientOptions());

            var fixture = new Fixture();
            var name = fixture.Create<string>();

            var request = new HttpRequestMessage(HttpMethod.Put, $"domains/{name}")
            {
                Headers = { { "Authorization", $"Bearer {token}" } }
            };

            // Act
            var response = await client.SendAsync(request);
            _output.WriteLine(response.GetHeadersAsString());

            // Assert
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task Upsert_should_return_unauthorized_when_authorization_header_is_empty()
        {
            // Arrange
            var client = _factory.CreateClient(new WebApplicationFactoryClientOptions());

            var fixture = new Fixture();
            var name = fixture.Create<string>();

            var request = new HttpRequestMessage(HttpMethod.Put, $"domains/{name}");

            // Act
            var response = await client.SendAsync(request);
            _output.WriteLine(response.GetHeadersAsString());

            // Assert
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task Upsert_should_return_ok_when_service_does_not_throw_an_exception()
        {
            // Arrange
            var fixture = new Fixture();

            var name = fixture.Create<string>();
            var domain = fixture.Create<Domain>();

            var domainServiceMock = new Mock<IDomainService>();

            domainServiceMock
                .Setup(x => x.UpsertAsync(It.IsAny<Domain>()))
                .Returns(Task.CompletedTask);

            var messageRepositoryMock = new Mock<IMessageRepository>();
            var messageSenderMock = new Mock<IMessageSender>();

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(domainServiceMock.Object);
                    services.AddSingleton(messageRepositoryMock.Object);
                    services.AddSingleton(messageSenderMock.Object);
                });

            }).CreateClient(new WebApplicationFactoryClientOptions());

            var request = new HttpRequestMessage(HttpMethod.Put, $"domains/{name}")
            {
                Headers = { { "Authorization", $"Bearer {TestApiUsersData.TOKEN_SUPERUSER_EXPIRE_20330518}" } },
                Content = JsonContent.Create(domain)
            };

            // Act
            var response = await client.SendAsync(request);
            _output.WriteLine(response.GetHeadersAsString());

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task Upsert_should_return_internal_server_error_when_service_throw_an_exception()
        {
            // Arrange
            var fixture = new Fixture();

            var name = fixture.Create<string>();
            var domain = fixture.Create<Domain>();

            var domainServiceMock = new Mock<IDomainService>();

            domainServiceMock
                .Setup(x => x.UpsertAsync(It.IsAny<Domain>()))
                .ThrowsAsync(new Exception());

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(domainServiceMock.Object);
                });

            }).CreateClient(new WebApplicationFactoryClientOptions());

            var request = new HttpRequestMessage(HttpMethod.Put, $"domains/{name}")
            {
                Headers = { { "Authorization", $"Bearer {TestApiUsersData.TOKEN_SUPERUSER_EXPIRE_20330518}" } },
                Content = JsonContent.Create(domain)
            };

            // Act
            var response = await client.SendAsync(request);
            _output.WriteLine(response.GetHeadersAsString());

            // Assert
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        }

        [Fact]
        public async Task GetPushFeatureStatus_should_not_require_token()
        {
            // Arrange
            var fixture = new Fixture();

            var domain = fixture.Create<Domain>();

            var domainServiceMock = new Mock<IDomainService>();
            domainServiceMock.Setup(x => x.GetByNameAsync(It.IsAny<string>()))
                .ReturnsAsync(domain);

            var messageRepositoryMock = new Mock<IMessageRepository>();
            var messageSenderMock = new Mock<IMessageSender>();

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(domainServiceMock.Object);
                    services.AddSingleton(messageRepositoryMock.Object);
                    services.AddSingleton(messageSenderMock.Object);
                });

            }).CreateClient(new WebApplicationFactoryClientOptions());

            var name = fixture.Create<string>();

            var request = new HttpRequestMessage(HttpMethod.Get, $"domains/{name}/isPushFeatureEnabled");

            // Act
            var response = await client.SendAsync(request);
            _output.WriteLine(response.GetHeadersAsString());

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task GetPushFeatureStatus_should_response_not_found_when_domain_service_return_null()
        {
            // Arrange
            var fixture = new Fixture();

            var name = fixture.Create<string>();
            Domain domain = null;

            var domainServiceMock = new Mock<IDomainService>();
            domainServiceMock.Setup(x => x.GetByNameAsync(name))
                .ReturnsAsync(domain);

            var messageRepositoryMock = new Mock<IMessageRepository>();
            var messageSenderMock = new Mock<IMessageSender>();

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(domainServiceMock.Object);
                    services.AddSingleton(messageRepositoryMock.Object);
                    services.AddSingleton(messageSenderMock.Object);
                });

            }).CreateClient(new WebApplicationFactoryClientOptions());

            var request = new HttpRequestMessage(HttpMethod.Get, $"domains/{name}/isPushFeatureEnabled");

            // Act
            var response = await client.SendAsync(request);
            _output.WriteLine(response.GetHeadersAsString());

            // Assert
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Theory]
        [InlineData(true, "true")]
        [InlineData(false, "false")]
        public async Task GetPushFeatureStatus_should_response_push_feature_status_returned_by_domain_service(bool isPushFeatureEnabledValue, string expectedResponse)
        {
            // Arrange
            var fixture = new Fixture();

            var name = fixture.Create<string>();
            var domain = fixture.Create<Domain>();
            domain.IsPushFeatureEnabled = isPushFeatureEnabledValue;

            var domainServiceMock = new Mock<IDomainService>();
            domainServiceMock.Setup(x => x.GetByNameAsync(name))
                .ReturnsAsync(domain);

            var messageRepositoryMock = new Mock<IMessageRepository>();
            var messageSenderMock = new Mock<IMessageSender>();

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(domainServiceMock.Object);
                    services.AddSingleton(messageRepositoryMock.Object);
                    services.AddSingleton(messageSenderMock.Object);
                });

            }).CreateClient(new WebApplicationFactoryClientOptions());

            var request = new HttpRequestMessage(HttpMethod.Get, $"domains/{name}/isPushFeatureEnabled");

            // Act
            var response = await client.SendAsync(request);
            _output.WriteLine(response.GetHeadersAsString());

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var isPushFeatureEnabledResponse = await response.Content.ReadAsStringAsync();
            Assert.Equal(expectedResponse, isPushFeatureEnabledResponse);
        }

        [Fact]
        public async Task GetPushFeatureStatus_should_response_internal_server_error_when_domain_service_throw_an_exception()
        {
            // Arrange
            var fixture = new Fixture();

            var name = fixture.Create<string>();
            var domain = fixture.Create<Domain>();

            var domainServiceMock = new Mock<IDomainService>();
            domainServiceMock.Setup(x => x.GetByNameAsync(name))
                .ThrowsAsync(new Exception());

            var messageRepositoryMock = new Mock<IMessageRepository>();
            var messageSenderMock = new Mock<IMessageSender>();

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(domainServiceMock.Object);
                    services.AddSingleton(messageRepositoryMock.Object);
                    services.AddSingleton(messageSenderMock.Object);
                });

            }).CreateClient(new WebApplicationFactoryClientOptions());

            var request = new HttpRequestMessage(HttpMethod.Get, $"domains/{name}/isPushFeatureEnabled");

            // Act
            var response = await client.SendAsync(request);
            _output.WriteLine(response.GetHeadersAsString());

            // Assert
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

            var content = await response.Content.ReadAsStringAsync();
            Assert.Contains("unexpected error", content, StringComparison.OrdinalIgnoreCase);
        }

        [Theory]
        [InlineData(TestApiUsersData.TOKEN_EMPTY)]
        [InlineData(TestApiUsersData.TOKEN_BROKEN)]
        [InlineData(TestApiUsersData.TOKEN_SUPERUSER_EXPIRE_20010908)]
        public async Task GetDomain_should_return_unauthorized_when_token_is_not_valid(string token)
        {
            // Arrange
            var client = _factory.CreateClient(new WebApplicationFactoryClientOptions());

            var fixture = new Fixture();
            var name = fixture.Create<string>();

            var request = new HttpRequestMessage(HttpMethod.Get, $"domains/{name}")
            {
                Headers = { { "Authorization", $"Bearer {token}" } }
            };

            // Act
            var response = await client.SendAsync(request);
            _output.WriteLine(response.GetHeadersAsString());

            // Assert
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Theory]
        [InlineData(TestApiUsersData.TOKEN_SUPERUSER_NOTDEFINED_EXPIRE_20330518)]
        [InlineData(TestApiUsersData.TOKEN_SUPERUSER_FALSE_EXPIRE_20330518)]
        public async Task GetDomain_should_return_forbidden_when_token_is_valid_but_a_wrong_isSU_flag(string token)
        {
            // Arrange
            var client = _factory.CreateClient(new WebApplicationFactoryClientOptions());

            var fixture = new Fixture();
            var name = fixture.Create<string>();

            var request = new HttpRequestMessage(HttpMethod.Get, $"domains/{name}")
            {
                Headers = { { "Authorization", $"Bearer {token}" } }
            };

            // Act
            var response = await client.SendAsync(request);
            _output.WriteLine(response.GetHeadersAsString());

            // Assert
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task GetDomain_should_return_not_found_when_domain_service_return_null()
        {
            // Arrange
            var fixture = new Fixture();

            var name = fixture.Create<string>();
            Domain domain = null;

            var domainServiceMock = new Mock<IDomainService>();
            domainServiceMock.Setup(x => x.GetByNameAsync(name))
                .ReturnsAsync(domain);

            var messageRepositoryMock = new Mock<IMessageRepository>();
            var messageSenderMock = new Mock<IMessageSender>();

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(domainServiceMock.Object);
                    services.AddSingleton(messageRepositoryMock.Object);
                    services.AddSingleton(messageSenderMock.Object);
                });

            }).CreateClient(new WebApplicationFactoryClientOptions());

            var request = new HttpRequestMessage(HttpMethod.Get, $"domains/{name}")
            {
                Headers = { { "Authorization", $"Bearer {TestApiUsersData.TOKEN_SUPERUSER_EXPIRE_20330518}" } },
            };

            // Act
            var response = await client.SendAsync(request);
            _output.WriteLine(response.GetHeadersAsString());

            // Assert
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Fact]
        public async Task GetDomain_should_return_domain_OK()
        {
            // Arrange
            var fixture = new Fixture();

            var name = fixture.Create<string>();
            var domain = fixture.Create<Domain>();
            domain.Name = name;

            var domainServiceMock = new Mock<IDomainService>();
            domainServiceMock.Setup(x => x.GetByNameAsync(name))
                .ReturnsAsync(domain);

            var messageRepositoryMock = new Mock<IMessageRepository>();
            var messageSenderMock = new Mock<IMessageSender>();

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(domainServiceMock.Object);
                    services.AddSingleton(messageRepositoryMock.Object);
                    services.AddSingleton(messageSenderMock.Object);
                });

            }).CreateClient(new WebApplicationFactoryClientOptions());

            var request = new HttpRequestMessage(HttpMethod.Get, $"domains/{name}")
            {
                Headers = { { "Authorization", $"Bearer {TestApiUsersData.TOKEN_SUPERUSER_EXPIRE_20330518}" } },
            };

            // Act
            var response = await client.SendAsync(request);
            _output.WriteLine(response.GetHeadersAsString());

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var responseDomain = JsonSerializer.Deserialize<Domain>(
                await response.Content.ReadAsStringAsync(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
            );

            Assert.NotNull(responseDomain);
            Assert.Equal(domain.Name, responseDomain.Name);
            Assert.Equal(domain.IsPushFeatureEnabled, responseDomain.IsPushFeatureEnabled);
            Assert.Equal(domain.UsesExternalPushDomain, responseDomain.UsesExternalPushDomain);
            Assert.Equal(domain.ExternalPushDomain, responseDomain.ExternalPushDomain);
        }

        [Fact]
        public async Task GetDomain_should_return_internal_server_error_when_domain_service_throw_an_exception()
        {
            // Arrange
            var fixture = new Fixture();

            var name = fixture.Create<string>();
            var domain = fixture.Create<Domain>();

            var domainServiceMock = new Mock<IDomainService>();
            domainServiceMock.Setup(x => x.GetByNameAsync(name))
                .ThrowsAsync(new Exception());

            var messageRepositoryMock = new Mock<IMessageRepository>();
            var messageSenderMock = new Mock<IMessageSender>();

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(domainServiceMock.Object);
                    services.AddSingleton(messageRepositoryMock.Object);
                    services.AddSingleton(messageSenderMock.Object);
                });

            }).CreateClient(new WebApplicationFactoryClientOptions());

            var request = new HttpRequestMessage(HttpMethod.Get, $"domains/{name}")
            {
                Headers = { { "Authorization", $"Bearer {TestApiUsersData.TOKEN_SUPERUSER_EXPIRE_20330518}" } },
            };

            // Act
            var response = await client.SendAsync(request);
            _output.WriteLine(response.GetHeadersAsString());

            // Assert
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

            var content = await response.Content.ReadAsStringAsync();
            Assert.Contains("unexpected error", content, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task GetPushConfiguration_should_not_require_token()
        {
            // Arrange
            var fixture = new Fixture();

            var name = fixture.Create<string>();
            var domain = fixture.Create<Domain>();
            domain.Name = name;

            var domainServiceMock = new Mock<IDomainService>();
            domainServiceMock.Setup(x => x.GetByNameAsync(It.IsAny<string>()))
                .ReturnsAsync(domain);

            var messageRepositoryMock = new Mock<IMessageRepository>();
            var messageSenderMock = new Mock<IMessageSender>();

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(domainServiceMock.Object);
                    services.AddSingleton(messageRepositoryMock.Object);
                    services.AddSingleton(messageSenderMock.Object);
                });

            }).CreateClient(new WebApplicationFactoryClientOptions());

            var request = new HttpRequestMessage(HttpMethod.Get, $"domains/{name}/push-configuration");

            // Act
            var response = await client.SendAsync(request);
            _output.WriteLine(response.GetHeadersAsString());

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task GetPushConfiguration_should_return_not_found_when_domainservice_return_null()
        {
            // Arrange
            var fixture = new Fixture();

            var name = fixture.Create<string>();
            Domain domain = null;

            var domainServiceMock = new Mock<IDomainService>();
            domainServiceMock.Setup(x => x.GetByNameAsync(name))
                .ReturnsAsync(domain);

            var messageRepositoryMock = new Mock<IMessageRepository>();
            var messageSenderMock = new Mock<IMessageSender>();

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(domainServiceMock.Object);
                    services.AddSingleton(messageRepositoryMock.Object);
                    services.AddSingleton(messageSenderMock.Object);
                });

            }).CreateClient(new WebApplicationFactoryClientOptions());

            var request = new HttpRequestMessage(HttpMethod.Get, $"domains/{name}/push-configuration");

            // Act
            var response = await client.SendAsync(request);
            _output.WriteLine(response.GetHeadersAsString());

            // Assert
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Fact]
        public async Task GetPushConfiguration_should_return_pushconfiguration_OK()
        {
            // Arrange
            var fixture = new Fixture();

            var name = fixture.Create<string>();
            var domain = fixture.Create<Domain>();
            domain.Name = name;

            var domainServiceMock = new Mock<IDomainService>();
            domainServiceMock.Setup(x => x.GetByNameAsync(name))
                .ReturnsAsync(domain);

            var messageRepositoryMock = new Mock<IMessageRepository>();
            var messageSenderMock = new Mock<IMessageSender>();

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(domainServiceMock.Object);
                    services.AddSingleton(messageRepositoryMock.Object);
                    services.AddSingleton(messageSenderMock.Object);
                });

            }).CreateClient(new WebApplicationFactoryClientOptions());

            var request = new HttpRequestMessage(HttpMethod.Get, $"domains/{name}/push-configuration");

            // Act
            var response = await client.SendAsync(request);
            _output.WriteLine(response.GetHeadersAsString());

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var responseDomainPushConfiguration = JsonSerializer.Deserialize<DomainPushConfiguration>(
                await response.Content.ReadAsStringAsync(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
            );

            Assert.NotNull(responseDomainPushConfiguration);
            Assert.Equal(domain.IsPushFeatureEnabled, responseDomainPushConfiguration.IsPushFeatureEnabled);
            Assert.Equal(domain.UsesExternalPushDomain, responseDomainPushConfiguration.UsesExternalPushDomain);
            Assert.Equal(domain.ExternalPushDomain, responseDomainPushConfiguration.ExternalPushDomain);
        }

        [Fact]
        public async Task GetPushConfiguration_should_return_internal_server_error_when_domainservice_throw_an_exception()
        {
            // Arrange
            var fixture = new Fixture();

            var name = fixture.Create<string>();
            var domain = fixture.Create<Domain>();

            var domainServiceMock = new Mock<IDomainService>();
            domainServiceMock.Setup(x => x.GetByNameAsync(name))
                .ThrowsAsync(new Exception());

            var messageRepositoryMock = new Mock<IMessageRepository>();
            var messageSenderMock = new Mock<IMessageSender>();

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(domainServiceMock.Object);
                    services.AddSingleton(messageRepositoryMock.Object);
                    services.AddSingleton(messageSenderMock.Object);
                });

            }).CreateClient(new WebApplicationFactoryClientOptions());

            var request = new HttpRequestMessage(HttpMethod.Get, $"domains/{name}/push-configuration");

            // Act
            var response = await client.SendAsync(request);
            _output.WriteLine(response.GetHeadersAsString());

            // Assert
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

            var content = await response.Content.ReadAsStringAsync();
            Assert.Contains("unexpected error", content, StringComparison.OrdinalIgnoreCase);
        }
    }
}
