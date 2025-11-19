using AutoFixture;
using Doppler.PushContact.Controllers;
using Doppler.PushContact.Models;
using Doppler.PushContact.Models.DTOs;
using Doppler.PushContact.Models.Enums;
using Doppler.PushContact.Models.Models;
using Doppler.PushContact.Models.PushContactApiResponses;
using Doppler.PushContact.Services;
using Doppler.PushContact.Services.Messages;
using Doppler.PushContact.Test.Controllers.Utils;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Collections.Generic;
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
                .Setup(x => x.UpsertAsync(It.IsAny<DomainDTO>()))
                .Returns(Task.CompletedTask);

            var webPushEventServiceMock = new Mock<IWebPushEventService>();
            var messageStatsServiceMock = new Mock<IMessageStatsService>();

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(domainServiceMock.Object);
                    services.AddSingleton(webPushEventServiceMock.Object);
                    services.AddSingleton(messageStatsServiceMock.Object);
                    services.AddSingleton(new Mock<IMessageService>().Object);
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
                .Setup(x => x.UpsertAsync(It.IsAny<DomainDTO>()))
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

            var domain = fixture.Create<DomainDTO>();

            var domainServiceMock = new Mock<IDomainService>();
            domainServiceMock.Setup(x => x.GetByNameAsync(It.IsAny<string>()))
                .ReturnsAsync(domain);

            var webPushEventServiceMock = new Mock<IWebPushEventService>();
            var messageStatsServiceMock = new Mock<IMessageStatsService>();

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(domainServiceMock.Object);
                    services.AddSingleton(webPushEventServiceMock.Object);
                    services.AddSingleton(messageStatsServiceMock.Object);
                    services.AddSingleton(new Mock<IMessageService>().Object);
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
            DomainDTO domain = null;

            var domainServiceMock = new Mock<IDomainService>();
            domainServiceMock.Setup(x => x.GetByNameAsync(name))
                .ReturnsAsync(domain);

            var webPushEventServiceMock = new Mock<IWebPushEventService>();
            var messageStatsServiceMock = new Mock<IMessageStatsService>();

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(domainServiceMock.Object);
                    services.AddSingleton(webPushEventServiceMock.Object);
                    services.AddSingleton(messageStatsServiceMock.Object);
                    services.AddSingleton(new Mock<IMessageService>().Object);
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
            var domain = fixture.Create<DomainDTO>();
            domain.IsPushFeatureEnabled = isPushFeatureEnabledValue;

            var domainServiceMock = new Mock<IDomainService>();
            domainServiceMock.Setup(x => x.GetByNameAsync(name))
                .ReturnsAsync(domain);

            var webPushEventServiceMock = new Mock<IWebPushEventService>();
            var messageStatsServiceMock = new Mock<IMessageStatsService>();

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(domainServiceMock.Object);
                    services.AddSingleton(webPushEventServiceMock.Object);
                    services.AddSingleton(messageStatsServiceMock.Object);
                    services.AddSingleton(new Mock<IMessageService>().Object);
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

            var webPushEventServiceMock = new Mock<IWebPushEventService>();
            var messageStatsServiceMock = new Mock<IMessageStatsService>();

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(domainServiceMock.Object);
                    services.AddSingleton(webPushEventServiceMock.Object);
                    services.AddSingleton(messageStatsServiceMock.Object);
                    services.AddSingleton(new Mock<IMessageService>().Object);
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
            DomainDTO domain = null;

            var domainServiceMock = new Mock<IDomainService>();
            domainServiceMock.Setup(x => x.GetByNameAsync(name))
                .ReturnsAsync(domain);

            var webPushEventServiceMock = new Mock<IWebPushEventService>();
            var messageStatsServiceMock = new Mock<IMessageStatsService>();

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(domainServiceMock.Object);
                    services.AddSingleton(webPushEventServiceMock.Object);
                    services.AddSingleton(messageStatsServiceMock.Object);
                    services.AddSingleton(new Mock<IMessageService>().Object);
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
            var domain = fixture.Create<DomainDTO>();
            domain.Name = name;

            var domainServiceMock = new Mock<IDomainService>();
            domainServiceMock.Setup(x => x.GetByNameAsync(name))
                .ReturnsAsync(domain);

            var webPushEventServiceMock = new Mock<IWebPushEventService>();
            var messageStatsServiceMock = new Mock<IMessageStatsService>();

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(domainServiceMock.Object);
                    services.AddSingleton(webPushEventServiceMock.Object);
                    services.AddSingleton(messageStatsServiceMock.Object);
                    services.AddSingleton(new Mock<IMessageService>().Object);
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

            var webPushEventServiceMock = new Mock<IWebPushEventService>();
            var messageStatsServiceMock = new Mock<IMessageStatsService>();

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(domainServiceMock.Object);
                    services.AddSingleton(webPushEventServiceMock.Object);
                    services.AddSingleton(messageStatsServiceMock.Object);
                    services.AddSingleton(new Mock<IMessageService>().Object);
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
            var domain = fixture.Create<DomainDTO>();
            domain.Name = name;

            var domainServiceMock = new Mock<IDomainService>();
            domainServiceMock.Setup(x => x.GetByNameAsync(It.IsAny<string>()))
                .ReturnsAsync(domain);

            var webPushEventServiceMock = new Mock<IWebPushEventService>();
            var messageStatsServiceMock = new Mock<IMessageStatsService>();

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(domainServiceMock.Object);
                    services.AddSingleton(webPushEventServiceMock.Object);
                    services.AddSingleton(messageStatsServiceMock.Object);
                    services.AddSingleton(new Mock<IMessageService>().Object);
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
            DomainDTO domain = null;

            var domainServiceMock = new Mock<IDomainService>();
            domainServiceMock.Setup(x => x.GetByNameAsync(name))
                .ReturnsAsync(domain);

            var webPushEventServiceMock = new Mock<IWebPushEventService>();
            var messageStatsServiceMock = new Mock<IMessageStatsService>();

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(domainServiceMock.Object);
                    services.AddSingleton(webPushEventServiceMock.Object);
                    services.AddSingleton(messageStatsServiceMock.Object);
                    services.AddSingleton(new Mock<IMessageService>().Object);
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
            var domain = fixture.Create<DomainDTO>();
            domain.Name = name;

            var domainServiceMock = new Mock<IDomainService>();
            domainServiceMock.Setup(x => x.GetByNameAsync(name))
                .ReturnsAsync(domain);

            var webPushEventServiceMock = new Mock<IWebPushEventService>();
            var messageStatsServiceMock = new Mock<IMessageStatsService>();

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(domainServiceMock.Object);
                    services.AddSingleton(webPushEventServiceMock.Object);
                    services.AddSingleton(messageStatsServiceMock.Object);
                    services.AddSingleton(new Mock<IMessageService>().Object);
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

            var webPushEventServiceMock = new Mock<IWebPushEventService>();
            var messageStatsServiceMock = new Mock<IMessageStatsService>();

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(domainServiceMock.Object);
                    services.AddSingleton(webPushEventServiceMock.Object);
                    services.AddSingleton(messageStatsServiceMock.Object);
                    services.AddSingleton(new Mock<IMessageService>().Object);
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

        [Theory]
        [InlineData(TestApiUsersData.TOKEN_EMPTY)]
        [InlineData(TestApiUsersData.TOKEN_BROKEN)]
        [InlineData(TestApiUsersData.TOKEN_SUPERUSER_EXPIRE_20010908)]
        public async Task GetDomainStats_should_return_unauthorized_when_token_is_not_valid(string token)
        {
            // Arrange
            var client = _factory.CreateClient(new WebApplicationFactoryClientOptions());

            var fixture = new Fixture();
            var name = fixture.Create<string>();

            var request = new HttpRequestMessage(HttpMethod.Get, $"domains/{name}/stats")
            {
                Headers = { { "Authorization", $"Bearer {token}" } },
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
        public async Task GetDomainStats_should_return_forbidden_when_token_is_valid_but_a_wrong_isSU_flag(string token)
        {
            // Arrange
            var client = _factory.CreateClient(new WebApplicationFactoryClientOptions());

            var fixture = new Fixture();
            var name = fixture.Create<string>();

            var request = new HttpRequestMessage(HttpMethod.Get, $"domains/{name}/stats")
            {
                Headers = { { "Authorization", $"Bearer {token}" } },
            };

            // Act
            var response = await client.SendAsync(request);
            _output.WriteLine(response.GetHeadersAsString());

            // Assert
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task GetDomainStats_should_return_OK_with_expected_stats()
        {
            // Arrange
            var fixture = new Fixture();
            var name = fixture.Create<string>();

            var domain = fixture.Build<DomainDTO>()
                .With(x => x.Name, name)
                .Create();

            var contactsStats = fixture.Build<ContactsStatsDTO>()
                .With(x => x.DomainName, name)
                .With(x => x.Active, 5)
                .With(x => x.Deleted, 2)
                .With(x => x.Total, 7)
                .Create();

            var domainServiceMock = new Mock<IDomainService>();

            domainServiceMock.Setup(x => x.GetByNameAsync(name))
                .ReturnsAsync(domain);

            domainServiceMock.Setup(x => x.GetDomainContactStatsAsync(name))
                .ReturnsAsync(contactsStats);

            var webPushEventServiceMock = new Mock<IWebPushEventService>();
            var messageStatsServiceMock = new Mock<IMessageStatsService>();

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(domainServiceMock.Object);
                    services.AddSingleton(webPushEventServiceMock.Object);
                    services.AddSingleton(messageStatsServiceMock.Object);
                    services.AddSingleton(new Mock<IMessageService>().Object);
                });
            }).CreateClient(new WebApplicationFactoryClientOptions());

            var request = new HttpRequestMessage(HttpMethod.Get, $"domains/{name}/stats")
            {
                Headers = { { "Authorization", $"Bearer {TestApiUsersData.TOKEN_SUPERUSER_EXPIRE_20330518}" } },
            };

            // Act
            var response = await client.SendAsync(request);
            _output.WriteLine(response.GetHeadersAsString());

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var domainStats = JsonSerializer.Deserialize<DomainStats>(
                await response.Content.ReadAsStringAsync(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
            );

            Assert.NotNull(domainStats);
            Assert.Equal(name, domainStats.Name);
            Assert.Equal(5, domainStats.ContactsStats.Active);
            Assert.Equal(2, domainStats.ContactsStats.Deleted);
            Assert.Equal(7, domainStats.ContactsStats.Total);
        }

        [Fact]
        public async Task GetDomainStats_should_return_internal_server_error_when_service_throws_exception()
        {
            // Arrange
            var fixture = new Fixture();
            var name = fixture.Create<string>();

            var domain = fixture.Build<DomainDTO>()
                .With(x => x.Name, name)
                .Create();

            var webPushEventServiceMock = new Mock<IWebPushEventService>();
            var domainServiceMock = new Mock<IDomainService>();

            domainServiceMock.Setup(x => x.GetByNameAsync(name))
                .ReturnsAsync(domain);

            domainServiceMock.Setup(x => x.GetDomainContactStatsAsync(name))
                .ThrowsAsync(new Exception());

            var messageStatsServiceMock = new Mock<IMessageStatsService>();

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(domainServiceMock.Object);
                    services.AddSingleton(webPushEventServiceMock.Object);
                    services.AddSingleton(messageStatsServiceMock.Object);
                    services.AddSingleton(new Mock<IMessageService>().Object);
                });
            }).CreateClient(new WebApplicationFactoryClientOptions());

            var request = new HttpRequestMessage(HttpMethod.Get, $"domains/{name}/stats")
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
        public async Task GetDomainStats_should_return_notfound_when_domain_doesnt_exists()
        {
            // Arrange
            var fixture = new Fixture();
            var name = fixture.Create<string>();

            DomainDTO domain = null;

            var domainServiceMock = new Mock<IDomainService>();
            domainServiceMock.Setup(x => x.GetByNameAsync(name))
                .ReturnsAsync(domain);

            var webPushEventServiceMock = new Mock<IWebPushEventService>();
            var messageStatsServiceMock = new Mock<IMessageStatsService>();

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(domainServiceMock.Object);
                    services.AddSingleton(webPushEventServiceMock.Object);
                    services.AddSingleton(messageStatsServiceMock.Object);
                    services.AddSingleton(new Mock<IMessageService>().Object);
                });
            }).CreateClient(new WebApplicationFactoryClientOptions());

            var request = new HttpRequestMessage(HttpMethod.Get, $"domains/{name}/stats")
            {
                Headers = { { "Authorization", $"Bearer {TestApiUsersData.TOKEN_SUPERUSER_EXPIRE_20330518}" } },
            };

            // Act
            var response = await client.SendAsync(request);
            _output.WriteLine(response.GetHeadersAsString());

            // Assert
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Theory]
        [InlineData(TestApiUsersData.TOKEN_EMPTY)]
        [InlineData(TestApiUsersData.TOKEN_BROKEN)]
        [InlineData(TestApiUsersData.TOKEN_SUPERUSER_EXPIRE_20010908)]
        public async Task GetConsumedSends_should_return_unauthorized_when_token_is_not_valid(string token)
        {
            // Arrange
            var client = _factory.CreateClient(new WebApplicationFactoryClientOptions());

            var fixture = new Fixture();
            var domain = fixture.Create<string>();
            var from = DateTimeOffset.UtcNow.AddDays(-1);
            var to = DateTimeOffset.UtcNow;

            var request = new HttpRequestMessage(HttpMethod.Get, $"domains/{domain}/push-sends-consumed-count?from={from}&to={to}")
            {
                Headers = { { "Authorization", $"Bearer {token}" } },
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
        public async Task GetConsumedSends_should_return_forbidden_when_token_is_valid_but_a_wrong_isSU_flag(string token)
        {
            // Arrange
            var client = _factory.CreateClient(new WebApplicationFactoryClientOptions());

            var fixture = new Fixture();
            var domain = fixture.Create<string>();
            var from = DateTimeOffset.UtcNow.AddDays(-1);
            var to = DateTimeOffset.UtcNow;

            var request = new HttpRequestMessage(HttpMethod.Get, $"domains/{domain}/push-sends-consumed-count?from={from}&to={to}")
            {
                Headers = { { "Authorization", $"Bearer {token}" } },
            };

            // Act
            var response = await client.SendAsync(request);
            _output.WriteLine(response.GetHeadersAsString());

            // Assert
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task GetConsumedSends_should_return_internal_server_error_when_service_throws_exception()
        {
            // Arrange
            var fixture = new Fixture();
            var domain = fixture.Create<string>();
            var from = DateTime.UtcNow.AddDays(-1);
            var to = DateTime.UtcNow;

            var domainServiceMock = new Mock<IDomainService>();
            var messageServiceMock = new Mock<IMessageService>();
            var webPushEventServiceMock = new Mock<IWebPushEventService>();

            var messageStatsServiceMock = new Mock<IMessageStatsService>();
            messageStatsServiceMock.Setup(x => x.GetMessageStatsAsync(domain, null, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>()))
                .ThrowsAsync(new Exception());

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(domainServiceMock.Object);
                    services.AddSingleton(webPushEventServiceMock.Object);
                    services.AddSingleton(messageStatsServiceMock.Object);
                    services.AddSingleton(messageServiceMock.Object);
                });
            }).CreateClient(new WebApplicationFactoryClientOptions());

            var request = new HttpRequestMessage(HttpMethod.Get, $"domains/{domain}/push-sends-consumed-count?from={from}&to={to}")
            {
                Headers = { { "Authorization", $"Bearer {TestApiUsersData.TOKEN_SUPERUSER_EXPIRE_20330518}" } },
            };

            // Act
            var response = await client.SendAsync(request);
            _output.WriteLine(response.GetHeadersAsString());

            // Assert
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

            var content = await response.Content.ReadAsStringAsync();
            Assert.Contains("Unexpected error obtaining consumed. Try again.", content, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task GetConsumedSends_should_return_OK_with_expected_consumed_quantity()
        {
            // Arrange
            var fixture = new Fixture();
            var domain = fixture.Create<string>();
            var from = DateTime.UtcNow.AddDays(-1);
            var to = DateTime.UtcNow;

            var messageStats = new MessageStatsDTO()
            {
                Domain = domain,
                MessageId = Guid.Empty,
                DateFrom = from,
                DateTo = to,
                BillableSends = 11,
            };

            var domainServiceMock = new Mock<IDomainService>();
            var messageServiceMock = new Mock<IMessageService>();
            var webPushEventServiceMock = new Mock<IWebPushEventService>();

            var messageStatsServiceMock = new Mock<IMessageStatsService>();
            messageStatsServiceMock.Setup(x => x.GetMessageStatsAsync(domain, null, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>()))
                .ReturnsAsync(messageStats);

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(domainServiceMock.Object);
                    services.AddSingleton(webPushEventServiceMock.Object);
                    services.AddSingleton(messageStatsServiceMock.Object);
                    services.AddSingleton(messageServiceMock.Object);
                });
            }).CreateClient(new WebApplicationFactoryClientOptions());

            var request = new HttpRequestMessage(HttpMethod.Get, $"domains/{domain}/push-sends-consumed-count?from={from}&to={to}")
            {
                Headers = { { "Authorization", $"Bearer {TestApiUsersData.TOKEN_SUPERUSER_EXPIRE_20330518}" } },
            };

            // Act
            var response = await client.SendAsync(request);
            _output.WriteLine(response.GetHeadersAsString());

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var result = JsonSerializer.Deserialize<PushSendsConsumedResponse>(
                await response.Content.ReadAsStringAsync(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
            );

            Assert.NotNull(result);
            Assert.Equal(domain, result.Domain);
            Assert.Equal(messageStats.BillableSends, result.Consumed);
        }

        [Fact]
        public async Task GetMessageStats_should_return_ok_with_stats_obtained_from_messageStats_when_fromdate_is_lower_than_retentiondays()
        {
            // Arrange
            var fixture = new Fixture();

            var domain = fixture.Create<string>();
            var messageId = fixture.Create<Guid>();

            // from is lower than MessageStatsRetentionDays (360 days)
            var from = DateTime.UtcNow.AddDays(-1);
            var to = DateTime.UtcNow;

            var messageStatsDTO = new MessageStatsDTO()
            {
                MessageId = messageId,
                Domain = domain,
                Sent = 10,
                Delivered = 9,
                NotDelivered = 1,
                BillableSends = 10,
                Received = 7,
                Click = 2,
                ActionClick = 3,
            };

            var domainServiceMock = new Mock<IDomainService>();
            var webPushEventServiceMock = new Mock<IWebPushEventService>();
            var messageStatsServiceMock = new Mock<IMessageStatsService>();
            var messageServiceMock = new Mock<IMessageService>();

            messageStatsServiceMock
                .Setup(mr => mr.GetMessageStatsAsync(domain, messageId, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>()))
                .ReturnsAsync(messageStatsDTO);

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(domainServiceMock.Object);
                    services.AddSingleton(webPushEventServiceMock.Object);
                    services.AddSingleton(messageStatsServiceMock.Object);
                    services.AddSingleton(messageServiceMock.Object);
                });
            }).CreateClient(new WebApplicationFactoryClientOptions());

            var request = new HttpRequestMessage(HttpMethod.Get, $"domains/{domain}/messages/{messageId}/stats?from={from}&to={to}")
            {
                Headers = { { "Authorization", $"Bearer {TestApiUsersData.TOKEN_SUPERUSER_EXPIRE_20330518}" } },
            };

            // Act
            var response = await client.SendAsync(request);

            // Assert
            response.EnsureSuccessStatusCode(); // Check if the status code is 2xx

            var result = await response.Content.ReadFromJsonAsync<MessageDetailsResponse>();

            Assert.Equal(domain, result.Domain);
            Assert.Equal(messageId, result.MessageId);
            Assert.Equal(messageStatsDTO.Sent, result.Sent);
            Assert.Equal(messageStatsDTO.Delivered, result.Delivered);
            Assert.Equal(messageStatsDTO.NotDelivered, result.NotDelivered);
            Assert.Equal(messageStatsDTO.BillableSends, result.BillableSends);
            Assert.Equal(messageStatsDTO.Click, result.Clicks);
            Assert.Equal(messageStatsDTO.Received, result.Received);
            Assert.Equal(messageStatsDTO.ActionClick, result.ActionClick);
        }

        [Fact]
        public async Task GetMessageStats_should_return_ok_with_stats_obtained_from_messages_when_fromdate_is_greater_than_retentiondays()
        {
            // Arrange
            var fixture = new Fixture();

            var domain = fixture.Create<string>();
            var messageId = fixture.Create<Guid>();

            // from is greater than MessageStatsRetentionDays (360 days)
            var from = DateTime.UtcNow.AddDays(-720);
            var to = DateTime.UtcNow;

            var messageStatsDTO = new MessageStatsDTO()
            {
                MessageId = messageId,
                Domain = domain,
                Sent = 0,
            };

            var messageDetails = new MessageDetails()
            {
                MessageId = messageId,
                Domain = domain,
                Sent = 10,
                Delivered = 9,
                NotDelivered = 1,
                BillableSends = 10,
                Received = 7,
                Clicks = 2,
            };

            var domainServiceMock = new Mock<IDomainService>();
            var webPushEventServiceMock = new Mock<IWebPushEventService>();
            var messageStatsServiceMock = new Mock<IMessageStatsService>();
            var messageServiceMock = new Mock<IMessageService>();

            messageStatsServiceMock
                .Setup(mr => mr.GetMessageStatsAsync(domain, messageId, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>()))
                .ReturnsAsync(messageStatsDTO);

            messageServiceMock
                .Setup(mr => mr.GetMessageStatsAsync(domain, messageId, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>()))
                .ReturnsAsync(messageDetails);

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(domainServiceMock.Object);
                    services.AddSingleton(webPushEventServiceMock.Object);
                    services.AddSingleton(messageStatsServiceMock.Object);
                    services.AddSingleton(messageServiceMock.Object);
                });
            }).CreateClient(new WebApplicationFactoryClientOptions());

            var request = new HttpRequestMessage(HttpMethod.Get, $"domains/{domain}/messages/{messageId}/stats?from={from}&to={to}")
            {
                Headers = { { "Authorization", $"Bearer {TestApiUsersData.TOKEN_SUPERUSER_EXPIRE_20330518}" } },
            };

            // Act
            var response = await client.SendAsync(request);

            // Assert
            response.EnsureSuccessStatusCode(); // Check if the status code is 2xx

            var result = await response.Content.ReadFromJsonAsync<MessageDetailsResponse>();

            Assert.Equal(domain, result.Domain);
            Assert.Equal(messageId, result.MessageId);
            Assert.Equal(messageDetails.Sent, result.Sent);
            Assert.Equal(messageDetails.Delivered, result.Delivered);
            Assert.Equal(messageDetails.NotDelivered, result.NotDelivered);
            Assert.Equal(messageDetails.BillableSends, result.BillableSends);
            Assert.Equal(messageDetails.Clicks, result.Clicks);
            Assert.Equal(messageDetails.Received, result.Received);
        }

        [Fact]
        public async Task GetMessageStats_should_return_ok_but_stats_in_zero_when_service_throws_exception()
        {
            // Arrange
            var fixture = new Fixture();
            var domain = fixture.Create<string>();
            var messageId = fixture.Create<Guid>();

            var exceptionMessage = "testing exception";

            var messageDetailsWithStatsEmpty = new MessageDetails()
            {
                MessageId = messageId,
                Domain = domain,
                Sent = 0,
                Delivered = 0,
                NotDelivered = 0,
                BillableSends = 0,
                Clicks = 0,
                Received = 0,
            };

            var loggerMock = new Mock<ILogger<DomainController>>();
            var domainServiceMock = new Mock<IDomainService>();
            var webPushEventServiceMock = new Mock<IWebPushEventService>();
            var messageStatsServiceMock = new Mock<IMessageStatsService>();
            var messageServiceMock = new Mock<IMessageService>();

            messageStatsServiceMock
                .Setup(mr => mr.GetMessageStatsAsync(domain, messageId, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>()))
                .ThrowsAsync(new Exception(exceptionMessage));

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(domainServiceMock.Object);
                    services.AddSingleton(webPushEventServiceMock.Object);
                    services.AddSingleton(messageStatsServiceMock.Object);
                    services.AddSingleton(messageServiceMock.Object);
                    services.AddSingleton(loggerMock.Object);
                });
            }).CreateClient(new WebApplicationFactoryClientOptions());

            var from = DateTime.UtcNow.AddDays(-1);
            var to = DateTime.UtcNow;

            var request = new HttpRequestMessage(HttpMethod.Get, $"domains/{domain}/messages/{messageId}/stats?from={from}&to={to}")
            {
                Headers = { { "Authorization", $"Bearer {TestApiUsersData.TOKEN_SUPERUSER_EXPIRE_20330518}" } },
            };

            // Act
            var response = await client.SendAsync(request);

            // Assert
            response.EnsureSuccessStatusCode(); // Check if the status code is 2xx

            var result = await response.Content.ReadFromJsonAsync<MessageDetailsResponse>();

            Assert.Equal(domain, result.Domain);
            Assert.Equal(messageId, result.MessageId);
            Assert.Equal(0, result.Sent);
            Assert.Equal(0, result.Delivered);
            Assert.Equal(0, result.NotDelivered);
            Assert.Equal(0, result.BillableSends);
            Assert.Equal(0, result.Clicks);
            Assert.Equal(0, result.Received);
            Assert.Equal(0, result.ActionClick);

            loggerMock.Verify(
                x => x.Log(
                    It.Is<LogLevel>(l => l == LogLevel.Error),
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("An unexpected error occurred obtaining message stats.")),
                    It.Is<Exception>(e => e.Message == exceptionMessage),
                    It.Is<Func<It.IsAnyType, Exception, string>>((v, t) => true)),
                Times.Once);
        }

        [Fact]
        public async Task GetMessagesStatsGroupedByPeriod_ShouldReturnOk_WithValidStats()
        {
            // Arrange
            var fixture = new Fixture();

            var domain = fixture.Create<string>();
            var messageId1 = fixture.Create<Guid>();
            var messageIds = new List<Guid> { messageId1 };

            var from = DateTime.UtcNow.AddDays(-2);
            var to = DateTime.UtcNow;

            var periods = new List<MessageStatsPeriodDTO>
            {
                new MessageStatsPeriodDTO
                {
                    Date = DateTime.UtcNow,
                    Sent = 10,
                    Delivered = 8,
                    NotDelivered = 2,
                    Received = 7,
                    Click = 3,
                    ActionClick = 1,
                    BillableSends = 9
                },
                new MessageStatsPeriodDTO
                {
                    Date = DateTime.UtcNow,
                    Sent = 5,
                    Delivered = 3,
                    NotDelivered = 2,
                    Received = 3,
                    Click = 3,
                    ActionClick = 0,
                    BillableSends = 5
                }
            };

            var domainServiceMock = new Mock<IDomainService>();
            var webPushEventServiceMock = new Mock<IWebPushEventService>();
            var messageStatsServiceMock = new Mock<IMessageStatsService>();
            var messageServiceMock = new Mock<IMessageService>();
            var loggerMock = new Mock<ILogger<DomainController>>();

            messageStatsServiceMock
                .Setup(s => s.GetMessageStatsByPeriodAsync(domain, messageIds, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), MessageStatsGroupedPeriodEnum.Day))
                .ReturnsAsync(periods);

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(domainServiceMock.Object);
                    services.AddSingleton(webPushEventServiceMock.Object);
                    services.AddSingleton(messageStatsServiceMock.Object);
                    services.AddSingleton(messageServiceMock.Object);
                    services.AddSingleton(loggerMock.Object);
                });
            }).CreateClient(new WebApplicationFactoryClientOptions());

            var request = new HttpRequestMessage(HttpMethod.Get,
                $"domains/{domain}/messages/stats/grouped?messageIds={messageId1}&from={from}&to={to}&period=day")
            {
                Headers = { { "Authorization", $"Bearer {TestApiUsersData.TOKEN_SUPERUSER_EXPIRE_20330518}" } }
            };

            // Act
            var response = await client.SendAsync(request);

            // Assert
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<MessageStatsGroupedByPeriodModel>();

            Assert.Equal(domain, result.Domain);
            Assert.Equal(2, result.Periods.Count);
            Assert.Equal(15, result.Totals.Sent);
            Assert.Equal(11, result.Totals.Delivered);
            Assert.Equal(4, result.Totals.NotDelivered);
            Assert.Equal(10, result.Totals.Received);
            Assert.Equal(6, result.Totals.Click);
            Assert.Equal(1, result.Totals.ActionClick);
            Assert.Equal(14, result.Totals.BillableSends);
        }

        [Fact]
        public async Task GetMessagesStatsGroupedByPeriod_ShouldUseDefaultPeriod_WhenPeriodNotProvided()
        {
            // Arrange
            var fixture = new Fixture();

            var domain = fixture.Create<string>();
            var messageIds = new List<Guid> { fixture.Create<Guid>() };

            var from = DateTime.UtcNow.AddDays(-2);
            var to = DateTime.UtcNow;

            var messageStatsServiceMock = new Mock<IMessageStatsService>();
            var domainServiceMock = new Mock<IDomainService>();
            var webPushEventServiceMock = new Mock<IWebPushEventService>();
            var messageServiceMock = new Mock<IMessageService>();
            var loggerMock = new Mock<ILogger<DomainController>>();

            messageStatsServiceMock
                .Setup(s => s.GetMessageStatsByPeriodAsync(domain, messageIds, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), MessageStatsGroupedPeriodEnum.Day))
                .ReturnsAsync(new List<MessageStatsPeriodDTO>());

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(messageStatsServiceMock.Object);
                    services.AddSingleton(domainServiceMock.Object);
                    services.AddSingleton(webPushEventServiceMock.Object);
                    services.AddSingleton(messageServiceMock.Object);
                    services.AddSingleton(loggerMock.Object);
                });
            }).CreateClient();

            var request = new HttpRequestMessage(HttpMethod.Get,
                $"domains/{domain}/messages/stats/grouped?messageIds={messageIds[0]}&from={from}&to={to}")
            {
                Headers = { { "Authorization", $"Bearer {TestApiUsersData.TOKEN_SUPERUSER_EXPIRE_20330518}" } }
            };

            // Act
            var response = await client.SendAsync(request);

            // Assert
            response.EnsureSuccessStatusCode();

            messageStatsServiceMock.Verify(s =>
                s.GetMessageStatsByPeriodAsync(domain, messageIds, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), MessageStatsGroupedPeriodEnum.Day),
                Times.Once);
        }

        [Fact]
        public async Task GetMessagesStatsGroupedByPeriod_ShouldReturnBadRequest_WhenInvalidPeriod()
        {
            // Arrange
            var fixture = new Fixture();
            var domain = fixture.Create<string>();
            var messageId = fixture.Create<Guid>();

            var from = DateTime.UtcNow.AddDays(-1);
            var to = DateTime.UtcNow;

            var messageStatsServiceMock = new Mock<IMessageStatsService>();
            var domainServiceMock = new Mock<IDomainService>();
            var webPushEventServiceMock = new Mock<IWebPushEventService>();
            var messageServiceMock = new Mock<IMessageService>();
            var loggerMock = new Mock<ILogger<DomainController>>();

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(messageStatsServiceMock.Object);
                    services.AddSingleton(domainServiceMock.Object);
                    services.AddSingleton(webPushEventServiceMock.Object);
                    services.AddSingleton(messageServiceMock.Object);
                    services.AddSingleton(loggerMock.Object);
                });
            }).CreateClient();

            var request = new HttpRequestMessage(HttpMethod.Get,
                $"domains/{domain}/messages/stats/grouped?messageIds={messageId}&from={from}&to={to}&period=INVALID")
            {
                Headers = { { "Authorization", $"Bearer {TestApiUsersData.TOKEN_SUPERUSER_EXPIRE_20330518}" } }
            };

            // Act
            var response = await client.SendAsync(request);

            // Assert
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            var body = await response.Content.ReadAsStringAsync();
            Assert.Contains("Invalid period", body);
        }

        [Fact]
        public async Task GetMessagesStatsGroupedByPeriod_ShouldReturnBadRequest_WhenMessageIdsEmpty()
        {
            // Arrange
            var fixture = new Fixture();
            var domain = fixture.Create<string>();
            var from = DateTime.UtcNow.AddDays(-1);
            var to = DateTime.UtcNow;

            var messageStatsServiceMock = new Mock<IMessageStatsService>();
            var domainServiceMock = new Mock<IDomainService>();
            var webPushEventServiceMock = new Mock<IWebPushEventService>();
            var messageServiceMock = new Mock<IMessageService>();
            var loggerMock = new Mock<ILogger<DomainController>>();

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(messageStatsServiceMock.Object);
                    services.AddSingleton(domainServiceMock.Object);
                    services.AddSingleton(webPushEventServiceMock.Object);
                    services.AddSingleton(messageServiceMock.Object);
                    services.AddSingleton(loggerMock.Object);
                });
            }).CreateClient();

            var request = new HttpRequestMessage(HttpMethod.Get,
                $"domains/{domain}/messages/stats/grouped?from={from}&to={to}")
            {
                Headers = { { "Authorization", $"Bearer {TestApiUsersData.TOKEN_SUPERUSER_EXPIRE_20330518}" } }
            };

            // Act
            var response = await client.SendAsync(request);

            // Assert
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            var msg = await response.Content.ReadAsStringAsync();
            Assert.Contains("'messageIds' can not be empty", msg, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task GetMessagesStatsGroupedByPeriod_ShouldReturnTotalsZero_WhenPeriodsEmpty()
        {
            // Arrange
            var fixture = new Fixture();

            var domain = fixture.Create<string>();
            var messageIds = new List<Guid> { fixture.Create<Guid>() };
            var from = DateTime.UtcNow.AddDays(-2);
            var to = DateTime.UtcNow;

            var messageStatsServiceMock = new Mock<IMessageStatsService>();
            var domainServiceMock = new Mock<IDomainService>();
            var webPushEventServiceMock = new Mock<IWebPushEventService>();
            var messageServiceMock = new Mock<IMessageService>();
            var loggerMock = new Mock<ILogger<DomainController>>();

            messageStatsServiceMock
                .Setup(s => s.GetMessageStatsByPeriodAsync(domain, messageIds, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), MessageStatsGroupedPeriodEnum.Day))
                .ReturnsAsync(new List<MessageStatsPeriodDTO>()); // empty

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(messageStatsServiceMock.Object);
                    services.AddSingleton(domainServiceMock.Object);
                    services.AddSingleton(webPushEventServiceMock.Object);
                    services.AddSingleton(messageServiceMock.Object);
                    services.AddSingleton(loggerMock.Object);
                });
            }).CreateClient();

            var request = new HttpRequestMessage(HttpMethod.Get,
                $"domains/{domain}/messages/stats/grouped?messageIds={messageIds[0]}&from={from}&to={to}&period=day")
            {
                Headers = { { "Authorization", $"Bearer {TestApiUsersData.TOKEN_SUPERUSER_EXPIRE_20330518}" } }
            };

            // Act
            var response = await client.SendAsync(request);

            // Assert
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<MessageStatsGroupedByPeriodModel>();

            Assert.Equal([], result.Periods);
            Assert.Equal(0, result.Totals.Sent);
            Assert.Equal(0, result.Totals.Delivered);
            Assert.Equal(0, result.Totals.NotDelivered);
            Assert.Equal(0, result.Totals.ActionClick);
        }

        [Fact]
        public async Task GetMessagesStatsGroupedByPeriod_ShouldLogError_AndReturnOk_WithTotalsZero_WhenServiceThrowsException()
        {
            // Arrange
            var fixture = new Fixture();
            var domain = fixture.Create<string>();
            var messageIds = new List<Guid> { fixture.Create<Guid>() };
            var from = DateTime.UtcNow.AddDays(-1);
            var to = DateTime.UtcNow;

            var exceptionMessage = "Test exception";

            var messageStatsServiceMock = new Mock<IMessageStatsService>();
            var domainServiceMock = new Mock<IDomainService>();
            var webPushEventServiceMock = new Mock<IWebPushEventService>();
            var messageServiceMock = new Mock<IMessageService>();
            var loggerMock = new Mock<ILogger<DomainController>>();

            messageStatsServiceMock
                .Setup(s => s.GetMessageStatsByPeriodAsync(domain, messageIds, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), MessageStatsGroupedPeriodEnum.Day))
                .ThrowsAsync(new Exception(exceptionMessage));

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(messageStatsServiceMock.Object);
                    services.AddSingleton(domainServiceMock.Object);
                    services.AddSingleton(webPushEventServiceMock.Object);
                    services.AddSingleton(messageServiceMock.Object);
                    services.AddSingleton(loggerMock.Object);
                });
            }).CreateClient();

            var request = new HttpRequestMessage(HttpMethod.Get,
                $"domains/{domain}/messages/stats/grouped?messageIds={messageIds[0]}&from={from}&to={to}&period=day")
            {
                Headers = { { "Authorization", $"Bearer {TestApiUsersData.TOKEN_SUPERUSER_EXPIRE_20330518}" } }
            };

            // Act
            var response = await client.SendAsync(request);

            // Assert
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<MessageStatsGroupedByPeriodModel>();

            Assert.Equal([], result.Periods);
            Assert.Equal(0, result.Totals.Sent);
            Assert.Equal(0, result.Totals.Delivered);
            Assert.Equal(0, result.Totals.NotDelivered);
            Assert.Equal(0, result.Totals.ActionClick);

            loggerMock.Verify(
                x => x.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("unexpected error occurred obtaining message stats")),
                    It.Is<Exception>(e => e.Message == exceptionMessage),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                Times.Once);
        }
    }
}
