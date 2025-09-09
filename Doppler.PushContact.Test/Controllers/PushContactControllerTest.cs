using AutoFixture;
using Doppler.PushContact.ApiModels;
using Doppler.PushContact.Controllers;
using Doppler.PushContact.DTOs;
using Doppler.PushContact.Models;
using Doppler.PushContact.Models.DTOs;
using Doppler.PushContact.Models.Enums;
using Doppler.PushContact.Models.PushContactApiResponses;
using Doppler.PushContact.Repositories.Interfaces;
using Doppler.PushContact.Services;
using Doppler.PushContact.Services.Messages;
using Doppler.PushContact.Services.Queue;
using Doppler.PushContact.Test.Controllers.Utils;
using Doppler.PushContact.Test.Dummies;
using Doppler.PushContact.Transversal;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Doppler.PushContact.Test.Controllers
{
    public class PushContactControllerTest : IClassFixture<WebApplicationFactory<Startup>>
    {
        private readonly WebApplicationFactory<Startup> _factory;
        private readonly ITestOutputHelper _output;

        public PushContactControllerTest(WebApplicationFactory<Startup> factory, ITestOutputHelper output)
        {
            _factory = factory;
            _output = output;
        }

        [Fact]
        public async Task Add_should_not_require_token()
        {
            // Arrange
            var fixture = new Fixture();

            var pushContactServiceMock = new Mock<IPushContactService>();
            var messageRepositoryMock = new Mock<IMessageRepository>();
            var webPushEventServiceMock = new Mock<IWebPushEventService>();

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(pushContactServiceMock.Object);
                    services.AddSingleton(messageRepositoryMock.Object);
                    services.AddSingleton(webPushEventServiceMock.Object);
                });

            }).CreateClient(new WebApplicationFactoryClientOptions());

            var request = new HttpRequestMessage(HttpMethod.Post, "push-contacts")
            {
                Content = JsonContent.Create(fixture.Create<PushContactModel>())
            };

            // Act
            var response = await client.SendAsync(request);
            _output.WriteLine(response.GetHeadersAsString());

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Theory]
        [InlineData(TestApiUsersData.TOKEN_EMPTY)]
        [InlineData(TestApiUsersData.TOKEN_BROKEN)]
        [InlineData(TestApiUsersData.TOKEN_SUPERUSER_NOTDEFINED_EXPIRE_20330518)]
        [InlineData(TestApiUsersData.TOKEN_SUPERUSER_EXPIRE_20330518)]
        [InlineData(TestApiUsersData.TOKEN_SUPERUSER_EXPIRE_20010908)]
        [InlineData(TestApiUsersData.TOKEN_SUPERUSER_FALSE_EXPIRE_20330518)]
        [InlineData(TestApiUsersData.TOKEN_ACCOUNT_123_TEST1_AT_TEST_DOT_COM_EXPIRE_20330518)]
        public async Task Add_should_accept_any_token(string token)
        {
            // Arrange
            var fixture = new Fixture();

            var pushContactServiceMock = new Mock<IPushContactService>();
            var messageRepositoryMock = new Mock<IMessageRepository>();
            var webPushEventServiceMock = new Mock<IWebPushEventService>();

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(pushContactServiceMock.Object);
                    services.AddSingleton(messageRepositoryMock.Object);
                    services.AddSingleton(webPushEventServiceMock.Object);
                });

            }).CreateClient(new WebApplicationFactoryClientOptions());

            var request = new HttpRequestMessage(HttpMethod.Post, "push-contacts")
            {
                Headers = { { "Authorization", $"Bearer {token}" } },
                Content = JsonContent.Create(fixture.Create<PushContactModel>())
            };

            // Act
            var response = await client.SendAsync(request);
            _output.WriteLine(response.GetHeadersAsString());

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Theory(Skip = "Now allows anonymous")]
        [InlineData(TestApiUsersData.TOKEN_EMPTY)]
        [InlineData(TestApiUsersData.TOKEN_BROKEN)]
        public async Task Add_should_return_unauthorized_when_token_is_not_valid(string token)
        {
            // Arrange
            var client = _factory.CreateClient(new WebApplicationFactoryClientOptions());

            var request = new HttpRequestMessage(HttpMethod.Post, "push-contacts")
            {
                Headers = { { "Authorization", $"Bearer {token}" } }
            };

            // Act
            var response = await client.SendAsync(request);
            _output.WriteLine(response.GetHeadersAsString());

            // Assert
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Theory(Skip = "Now allows anonymous")]
        [InlineData(TestApiUsersData.TOKEN_SUPERUSER_EXPIRE_20010908)]
        public async Task Add_should_return_unauthorized_when_token_is_a_expired_superuser_token(string token)
        {
            // Arrange
            var client = _factory.CreateClient(new WebApplicationFactoryClientOptions());

            var request = new HttpRequestMessage(HttpMethod.Post, "push-contacts")
            {
                Headers = { { "Authorization", $"Bearer {token}" } }
            };

            // Act
            var response = await client.SendAsync(request);
            _output.WriteLine(response.GetHeadersAsString());

            // Assert
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Theory(Skip = "Now allows anonymous")]
        [InlineData(TestApiUsersData.TOKEN_SUPERUSER_NOTDEFINED_EXPIRE_20330518)]
        [InlineData(TestApiUsersData.TOKEN_SUPERUSER_FALSE_EXPIRE_20330518)]
        [InlineData(TestApiUsersData.TOKEN_ACCOUNT_123_TEST1_AT_TEST_DOT_COM_EXPIRE_20330518)]
        public async Task Add_should_require_a_valid_token_with_isSU_flag(string token)
        {
            // Arrange
            var client = _factory.CreateClient(new WebApplicationFactoryClientOptions());

            var request = new HttpRequestMessage(HttpMethod.Post, "push-contacts")
            {
                Headers = { { "Authorization", $"Bearer {token}" } }
            };

            // Act
            var response = await client.SendAsync(request);
            _output.WriteLine(response.GetHeadersAsString());

            // Assert
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact(Skip = "Now allows anonymous")]
        public async Task Add_should_return_unauthorized_when_authorization_header_is_empty()
        {
            // Arrange
            var client = _factory.CreateClient(new WebApplicationFactoryClientOptions());

            var request = new HttpRequestMessage(HttpMethod.Post, "push-contacts");

            // Act
            var response = await client.SendAsync(request);
            _output.WriteLine(response.GetHeadersAsString());

            // Assert
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task Add_should_return_Ok()
        {
            // Arrange
            var fixture = new Fixture();

            var pushContactServiceMock = new Mock<IPushContactService>();
            var messageRepositoryMock = new Mock<IMessageRepository>();
            var webPushEventServiceMock = new Mock<IWebPushEventService>();

            pushContactServiceMock
                .Setup(x => x.AddAsync(It.IsAny<PushContactModel>()))
                .Returns(Task.CompletedTask);

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    // Remove QueueBackgroundService registered in Startup.cs.
                    // This is not removed automatically because it was registered using AddHostedService,
                    // which is different from if it had been registered with AddSingleton.
                    var descriptor = services.FirstOrDefault(
                        d => d.ServiceType == typeof(IHostedService) &&
                            d.ImplementationType == typeof(QueueBackgroundService));
                    if (descriptor != null)
                    {
                        services.Remove(descriptor);
                    }

                    // Now, replace by a dummy service
                    services.AddSingleton<IHostedService, NoOpBackgroundService>();

                    services.AddSingleton(pushContactServiceMock.Object);
                    services.AddSingleton(messageRepositoryMock.Object);
                    services.AddSingleton(webPushEventServiceMock.Object);
                });

            }).CreateClient(new WebApplicationFactoryClientOptions());

            var request = new HttpRequestMessage(HttpMethod.Post, "push-contacts")
            {
                Headers = { { "Authorization", $"Bearer {TestApiUsersData.TOKEN_SUPERUSER_EXPIRE_20330518}" } },
                Content = JsonContent.Create(fixture.Create<PushContactModel>())
            };

            // Act
            var response = await client.SendAsync(request);
            _output.WriteLine(response.GetHeadersAsString());

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task Add_should_trigger_visitor_registration_in_background()
        {
            // Arrange
            var fixture = new Fixture();

            var email = fixture.Create<string>();
            var domain = fixture.Create<string>();
            var visitorGuid = fixture.Create<string>();

            var pushContactServiceMock = new Mock<IPushContactService>();
            var messageRepositoryMock = new Mock<IMessageRepository>();
            var webPushEventServiceMock = new Mock<IWebPushEventService>();
            var dopplerHttpClientMock = new Mock<IDopplerHttpClient>();
            var backgroundQueueMock = new Mock<IBackgroundQueue>();

            Func<CancellationToken, Task> backgroundJob = null;

            backgroundQueueMock
                .Setup(q => q.QueueBackgroundQueueItem(It.IsAny<Func<CancellationToken, Task>>()))
                .Callback<Func<CancellationToken, Task>>(job => backgroundJob = job);

            pushContactServiceMock
                .Setup(x => x.AddAsync(It.IsAny<PushContactModel>()))
                .Returns(Task.CompletedTask);

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    // Remove QueueBackgroundService registered in Startup.cs.
                    // This is not removed automatically because it was registered using AddHostedService,
                    // which is different from if it had been registered with AddSingleton.
                    var descriptor = services.FirstOrDefault(
                        d => d.ServiceType == typeof(IHostedService) &&
                            d.ImplementationType == typeof(QueueBackgroundService));
                    if (descriptor != null)
                    {
                        services.Remove(descriptor);
                    }

                    // Now, replace by a dummy service
                    services.AddSingleton<IHostedService, NoOpBackgroundService>();

                    services.AddSingleton(pushContactServiceMock.Object);
                    services.AddSingleton(messageRepositoryMock.Object);
                    services.AddSingleton(webPushEventServiceMock.Object);
                    services.AddSingleton(dopplerHttpClientMock.Object);
                    services.AddSingleton(backgroundQueueMock.Object);
                });

            }).CreateClient(new WebApplicationFactoryClientOptions());


            var contact = new PushContactModel
            {
                Domain = domain,
                VisitorGuid = visitorGuid,
                Email = email,
            };

            var request = new HttpRequestMessage(HttpMethod.Post, "push-contacts")
            {
                Headers = { { "Authorization", $"Bearer {TestApiUsersData.TOKEN_SUPERUSER_EXPIRE_20330518}" } },
                Content = JsonContent.Create(contact)
            };

            // Act
            var response = await client.SendAsync(request);
            _output.WriteLine(response.GetHeadersAsString());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            Assert.NotNull(backgroundJob);
            await backgroundJob(CancellationToken.None);

            // Assert
            dopplerHttpClientMock.Verify(x =>
                x.RegisterVisitorSafeAsync(domain, visitorGuid, email),
                Times.Once);
        }

        [Fact]
        public async Task Add_should_returns_badRequest_when_service_thows_argumentException()
        {
            // Arrange
            var fixture = new Fixture();

            var pushContactServiceMock = new Mock<IPushContactService>();
            var messageRepositoryMock = new Mock<IMessageRepository>();
            var webPushEventServiceMock = new Mock<IWebPushEventService>();

            pushContactServiceMock
                .Setup(x => x.AddAsync(It.IsAny<PushContactModel>()))
                .ThrowsAsync(new ArgumentException());

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(pushContactServiceMock.Object);
                    services.AddSingleton(messageRepositoryMock.Object);
                    services.AddSingleton(webPushEventServiceMock.Object);
                });

            }).CreateClient(new WebApplicationFactoryClientOptions());

            var request = new HttpRequestMessage(HttpMethod.Post, "push-contacts")
            {
                Headers = { { "Authorization", $"Bearer {TestApiUsersData.TOKEN_SUPERUSER_EXPIRE_20330518}" } },
                Content = JsonContent.Create(fixture.Create<PushContactModel>())
            };

            // Act
            var response = await client.SendAsync(request);
            _output.WriteLine(response.GetHeadersAsString());

            // Assert
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task Add_should_return_internal_server_error_when_service_throw_an_exception()
        {
            // Arrange
            var fixture = new Fixture();

            var pushContactServiceMock = new Mock<IPushContactService>();
            var messageRepositoryMock = new Mock<IMessageRepository>();
            var webPushEventServiceMock = new Mock<IWebPushEventService>();
            var loggerMock = new Mock<ILogger<PushContactController>>();

            pushContactServiceMock
                .Setup(x => x.AddAsync(It.IsAny<PushContactModel>()))
                .ThrowsAsync(new Exception());

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(pushContactServiceMock.Object);
                    services.AddSingleton(messageRepositoryMock.Object);
                    services.AddSingleton(webPushEventServiceMock.Object);
                    services.AddSingleton(loggerMock.Object);
                });

            }).CreateClient(new WebApplicationFactoryClientOptions());

            var request = new HttpRequestMessage(HttpMethod.Post, "push-contacts")
            {
                Headers = { { "Authorization", $"Bearer {TestApiUsersData.TOKEN_SUPERUSER_EXPIRE_20330518}" } },
                Content = JsonContent.Create(fixture.Create<PushContactModel>())
            };

            // Act
            var response = await client.SendAsync(request);
            _output.WriteLine(response.GetHeadersAsString());

            // Assert
            // Assert
            Assert.Equal(StatusCodes.Status500InternalServerError, (int)response.StatusCode);
            loggerMock.Verify(
                x => x.Log(
                    It.Is<LogLevel>(l => l == LogLevel.Error),
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Unexpected error adding a new contact with token")),
                    It.IsAny<Exception>(),
                    It.Is<Func<It.IsAnyType, Exception, string>>((v, t) => true)),
                Times.Once);
        }

        [Fact]
        public async Task UpdateEmail_should_not_require_token()
        {
            // Arrange
            var fixture = new Fixture();

            var deviceToken = fixture.Create<string>();
            var email = fixture.Create<string>();

            var pushContactServiceMock = new Mock<IPushContactService>();
            var messageRepositoryMock = new Mock<IMessageRepository>();
            var webPushEventServiceMock = new Mock<IWebPushEventService>();

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(pushContactServiceMock.Object);
                    services.AddSingleton(messageRepositoryMock.Object);
                    services.AddSingleton(webPushEventServiceMock.Object);
                });

            }).CreateClient(new WebApplicationFactoryClientOptions());

            var request = new HttpRequestMessage(HttpMethod.Put, $"push-contacts/{deviceToken}/email")
            {
                Content = JsonContent.Create(email)
            };

            // Act
            var response = await client.SendAsync(request);
            _output.WriteLine(response.GetHeadersAsString());

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Theory]
        [InlineData(TestApiUsersData.TOKEN_EMPTY)]
        [InlineData(TestApiUsersData.TOKEN_BROKEN)]
        [InlineData(TestApiUsersData.TOKEN_SUPERUSER_NOTDEFINED_EXPIRE_20330518)]
        [InlineData(TestApiUsersData.TOKEN_SUPERUSER_EXPIRE_20330518)]
        [InlineData(TestApiUsersData.TOKEN_SUPERUSER_EXPIRE_20010908)]
        [InlineData(TestApiUsersData.TOKEN_SUPERUSER_FALSE_EXPIRE_20330518)]
        [InlineData(TestApiUsersData.TOKEN_ACCOUNT_123_TEST1_AT_TEST_DOT_COM_EXPIRE_20330518)]
        public async Task UpdateEmail_should_accept_any_token(string token)
        {
            // Arrange
            var fixture = new Fixture();

            var deviceToken = fixture.Create<string>();
            var email = fixture.Create<string>();

            var pushContactServiceMock = new Mock<IPushContactService>();
            var messageRepositoryMock = new Mock<IMessageRepository>();
            var webPushEventServiceMock = new Mock<IWebPushEventService>();

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(pushContactServiceMock.Object);
                    services.AddSingleton(messageRepositoryMock.Object);
                    services.AddSingleton(webPushEventServiceMock.Object);
                });

            }).CreateClient(new WebApplicationFactoryClientOptions());

            var request = new HttpRequestMessage(HttpMethod.Put, $"push-contacts/{deviceToken}/email")
            {
                Headers = { { "Authorization", $"Bearer {token}" } },
                Content = JsonContent.Create(email)
            };

            // Act
            var response = await client.SendAsync(request);
            _output.WriteLine(response.GetHeadersAsString());

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Theory(Skip = "Now allows anonymous")]
        [InlineData(TestApiUsersData.TOKEN_EMPTY)]
        [InlineData(TestApiUsersData.TOKEN_BROKEN)]
        public async Task UpdateEmail_should_return_unauthorized_when_token_is_not_valid(string token)
        {
            // Arrange
            var fixture = new Fixture();

            var deviceToken = fixture.Create<string>();
            var email = fixture.Create<string>();

            var client = _factory.CreateClient(new WebApplicationFactoryClientOptions());

            var request = new HttpRequestMessage(HttpMethod.Put, $"push-contacts/{deviceToken}/email")
            {
                Headers = { { "Authorization", $"Bearer {token}" } },
                Content = JsonContent.Create(email)
            };

            // Act
            var response = await client.SendAsync(request);
            _output.WriteLine(response.GetHeadersAsString());

            // Assert
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Theory(Skip = "Now allows anonymous")]
        [InlineData(TestApiUsersData.TOKEN_SUPERUSER_EXPIRE_20010908)]
        public async Task UpdateEmail_should_return_unauthorized_when_token_is_a_expired_superuser_token(string token)
        {
            // Arrange
            var fixture = new Fixture();

            var deviceToken = fixture.Create<string>();
            var email = fixture.Create<string>();

            var client = _factory.CreateClient(new WebApplicationFactoryClientOptions());

            var request = new HttpRequestMessage(HttpMethod.Put, $"push-contacts/{deviceToken}/email")
            {
                Headers = { { "Authorization", $"Bearer {token}" } },
                Content = JsonContent.Create(email)
            };

            // Act
            var response = await client.SendAsync(request);
            _output.WriteLine(response.GetHeadersAsString());

            // Assert
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Theory(Skip = "Now allows anonymous")]
        [InlineData(TestApiUsersData.TOKEN_SUPERUSER_NOTDEFINED_EXPIRE_20330518)]
        [InlineData(TestApiUsersData.TOKEN_SUPERUSER_FALSE_EXPIRE_20330518)]
        [InlineData(TestApiUsersData.TOKEN_ACCOUNT_123_TEST1_AT_TEST_DOT_COM_EXPIRE_20330518)]
        public async Task UpdateEmail_should_require_a_valid_token_with_isSU_flag(string token)
        {
            // Arrange
            var fixture = new Fixture();

            var deviceToken = fixture.Create<string>();
            var email = fixture.Create<string>();

            var client = _factory.CreateClient(new WebApplicationFactoryClientOptions());

            var request = new HttpRequestMessage(HttpMethod.Put, $"push-contacts/{deviceToken}/email")
            {
                Headers = { { "Authorization", $"Bearer {token}" } },
                Content = JsonContent.Create(email)
            };

            // Act
            var response = await client.SendAsync(request);
            _output.WriteLine(response.GetHeadersAsString());

            // Assert
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact(Skip = "Now allows anonymous")]
        public async Task UpdateEmail_should_return_unauthorized_when_authorization_header_is_empty()
        {
            // Arrange
            var fixture = new Fixture();

            var deviceToken = fixture.Create<string>();
            var email = fixture.Create<string>();

            var client = _factory.CreateClient(new WebApplicationFactoryClientOptions());

            var request = new HttpRequestMessage(HttpMethod.Put, $"push-contacts/{deviceToken}/email")
            {
                Content = JsonContent.Create(email)
            };

            // Act
            var response = await client.SendAsync(request);
            _output.WriteLine(response.GetHeadersAsString());

            // Assert
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task UpdateEmail_should_return_OK()
        {
            // Arrange
            var fixture = new Fixture();

            var deviceToken = fixture.Create<string>();
            var email = fixture.Create<string>();

            var pushContactServiceMock = new Mock<IPushContactService>();
            var messageRepositoryMock = new Mock<IMessageRepository>();
            var webPushEventServiceMock = new Mock<IWebPushEventService>();

            pushContactServiceMock
                .Setup(x => x.UpdateEmailAsync(It.IsAny<string>(), It.IsAny<string>()))
                .Returns(Task.CompletedTask);

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(pushContactServiceMock.Object);
                    services.AddSingleton(messageRepositoryMock.Object);
                    services.AddSingleton(webPushEventServiceMock.Object);
                });

            }).CreateClient(new WebApplicationFactoryClientOptions());

            var request = new HttpRequestMessage(HttpMethod.Put, $"push-contacts/{deviceToken}/email")
            {
                Headers = { { "Authorization", $"Bearer {TestApiUsersData.TOKEN_SUPERUSER_EXPIRE_20330518}" } },
                Content = JsonContent.Create(email)
            };

            // Act
            var response = await client.SendAsync(request);
            _output.WriteLine(response.GetHeadersAsString());

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task UpdateEmail_should_trigger_visitor_registration_in_background()
        {
            // Arrange
            var fixture = new Fixture();
            var deviceToken = fixture.Create<string>();
            var email = fixture.Create<string>();
            var domain = fixture.Create<string>();
            var visitorGuid = fixture.Create<string>();

            var visitorInfo = new VisitorInfoDTO
            {
                Domain = domain,
                VisitorGuid = visitorGuid,
                Email = email
            };

            var pushContactServiceMock = new Mock<IPushContactService>();
            var dopplerHttpClientMock = new Mock<IDopplerHttpClient>();
            var backgroundQueueMock = new Mock<IBackgroundQueue>();
            var messageRepositoryMock = new Mock<IMessageRepository>();
            var webPushEventServiceMock = new Mock<IWebPushEventService>();

            pushContactServiceMock
                .Setup(x => x.UpdateEmailAsync(It.IsAny<string>(), It.IsAny<string>()))
                .Returns(Task.CompletedTask);

            pushContactServiceMock
                .Setup(x => x.GetVisitorInfoSafeAsync(deviceToken))
                .ReturnsAsync(visitorInfo);

            Func<CancellationToken, Task> backgroundJob = null;

            backgroundQueueMock
                .Setup(q => q.QueueBackgroundQueueItem(It.IsAny<Func<CancellationToken, Task>>()))
                .Callback<Func<CancellationToken, Task>>(job => backgroundJob = job);

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    // Remove QueueBackgroundService registered in Startup.cs.
                    // This is not removed automatically because it was registered using AddHostedService,
                    // which is different from if it had been registered with AddSingleton.
                    var descriptor = services.FirstOrDefault(
                        d => d.ServiceType == typeof(IHostedService) &&
                            d.ImplementationType == typeof(QueueBackgroundService));
                    if (descriptor != null)
                    {
                        services.Remove(descriptor);
                    }

                    // Now, replace by a dummy service
                    services.AddSingleton<IHostedService, NoOpBackgroundService>();

                    services.AddSingleton(pushContactServiceMock.Object);
                    services.AddSingleton(dopplerHttpClientMock.Object);
                    services.AddSingleton(backgroundQueueMock.Object);
                    services.AddSingleton(messageRepositoryMock.Object);
                    services.AddSingleton(webPushEventServiceMock.Object);
                });
            }).CreateClient();

            var request = new HttpRequestMessage(HttpMethod.Put, $"push-contacts/{deviceToken}/email")
            {
                Headers = { { "Authorization", $"Bearer {TestApiUsersData.TOKEN_SUPERUSER_EXPIRE_20330518}" } },
                Content = JsonContent.Create(email)
            };

            // Act
            var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            Assert.NotNull(backgroundJob);
            await backgroundJob(CancellationToken.None);

            // Assert
            dopplerHttpClientMock.Verify(x =>
                x.RegisterVisitorSafeAsync(visitorInfo.Domain, visitorInfo.VisitorGuid, visitorInfo.Email),
                Times.Once);
        }

        [Fact]
        public async Task UpdateEmail_should_return_internal_server_error_when_service_throw_an_exception()
        {
            // TODO: corregir este test
            // Arrange
            var fixture = new Fixture();

            var deviceToken = fixture.Create<string>();
            var email = fixture.Create<string>();

            var pushContactServiceMock = new Mock<IPushContactService>();
            var loggerMock = new Mock<ILogger<PushContactController>>();

            pushContactServiceMock
                .Setup(x => x.UpdateEmailAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ThrowsAsync(new Exception());

            var pushMongoContextSettings = fixture.Create<PushMongoContextSettings>();
            var mongoDatabaseMock = new Mock<IMongoDatabase>();

            var mongoClientMock = new Mock<IMongoClient>();
            mongoClientMock
                .Setup(x => x.GetDatabase(pushMongoContextSettings.DatabaseName, null))
                .Returns(mongoDatabaseMock.Object);

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(pushContactServiceMock.Object);
                    services.AddSingleton(loggerMock.Object);
                    services.AddSingleton(mongoClientMock.Object);
                });

            }).CreateClient(new WebApplicationFactoryClientOptions());

            var request = new HttpRequestMessage(HttpMethod.Put, $"push-contacts/{deviceToken}/email")
            {
                Headers = { { "Authorization", $"Bearer {TestApiUsersData.TOKEN_SUPERUSER_EXPIRE_20330518}" } },
                Content = JsonContent.Create(email)
            };

            // Act
            var response = await client.SendAsync(request);
            _output.WriteLine(response.GetHeadersAsString());

            // Assert
            Assert.Equal(StatusCodes.Status500InternalServerError, (int)response.StatusCode);
            loggerMock.Verify(
                x => x.Log(
                    It.Is<LogLevel>(l => l == LogLevel.Error),
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Unexpected error updating the email:")),
                    It.IsAny<Exception>(),
                    It.Is<Func<It.IsAnyType, Exception, string>>((v, t) => true)),
                Times.Once);
        }

        [Theory]
        [InlineData(TestApiUsersData.TOKEN_EMPTY)]
        [InlineData(TestApiUsersData.TOKEN_BROKEN)]
        public async Task UpdatePushContactVisitorGuid_should_return_unauthorized_when_token_is_not_valid(string token)
        {
            // Arrange
            var fixture = new Fixture();

            var deviceToken = fixture.Create<string>();
            var visitorGuid = fixture.Create<string>();

            var client = _factory.CreateClient(new WebApplicationFactoryClientOptions());

            var request = new HttpRequestMessage(HttpMethod.Put, $"push-contacts/{deviceToken}/visitor-guid")
            {
                Headers = { { "Authorization", $"Bearer {token}" } },
                Content = JsonContent.Create(visitorGuid)
            };

            // Act
            var response = await client.SendAsync(request);
            _output.WriteLine(response.GetHeadersAsString());

            // Assert
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Theory]
        [InlineData(TestApiUsersData.TOKEN_SUPERUSER_EXPIRE_20010908)]
        public async Task UpdatePushContactVisitorGuid_should_return_unauthorized_when_token_is_a_expired_superuser_token(string token)
        {
            // Arrange
            var fixture = new Fixture();

            var deviceToken = fixture.Create<string>();
            var visitorGuid = fixture.Create<string>();

            var client = _factory.CreateClient(new WebApplicationFactoryClientOptions());

            var request = new HttpRequestMessage(HttpMethod.Put, $"push-contacts/{deviceToken}/visitor-guid")
            {
                Headers = { { "Authorization", $"Bearer {token}" } },
                Content = JsonContent.Create(visitorGuid)
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
        public async Task UpdatePushContactVisitorGuid_should_require_a_valid_token_with_isSU_flag(string token)
        {
            // Arrange
            var fixture = new Fixture();

            var deviceToken = fixture.Create<string>();
            var visitorGuid = fixture.Create<string>();

            var client = _factory.CreateClient(new WebApplicationFactoryClientOptions());

            var request = new HttpRequestMessage(HttpMethod.Put, $"push-contacts/{deviceToken}/visitor-guid")
            {
                Headers = { { "Authorization", $"Bearer {token}" } },
                Content = JsonContent.Create(visitorGuid)
            };

            // Act
            var response = await client.SendAsync(request);
            _output.WriteLine(response.GetHeadersAsString());

            // Assert
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task UpdatePushContactVisitorGuid_should_return_unauthorized_when_authorization_header_is_empty()
        {
            // Arrange
            var fixture = new Fixture();

            var deviceToken = fixture.Create<string>();
            var visitorGuid = fixture.Create<string>();

            var client = _factory.CreateClient(new WebApplicationFactoryClientOptions());

            var request = new HttpRequestMessage(HttpMethod.Put, $"push-contacts/{deviceToken}/visitor-guid")
            {
                Content = JsonContent.Create(visitorGuid)
            };

            // Act
            var response = await client.SendAsync(request);
            _output.WriteLine(response.GetHeadersAsString());

            // Assert
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task UpdatePushContactVisitorGuid_should_return_ok_when_service_does_not_throw_an_exception()
        {
            // Arrange
            var fixture = new Fixture();

            var deviceToken = fixture.Create<string>();
            var visitorGuid = fixture.Create<string>();

            var pushContactServiceMock = new Mock<IPushContactService>();
            var messageRepositoryMock = new Mock<IMessageRepository>();
            var webPushEventServiceMock = new Mock<IWebPushEventService>();

            pushContactServiceMock
                .Setup(x => x.UpdateEmailAsync(It.IsAny<string>(), It.IsAny<string>()))
                .Returns(Task.CompletedTask);

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(pushContactServiceMock.Object);
                    services.AddSingleton(messageRepositoryMock.Object);
                    services.AddSingleton(webPushEventServiceMock.Object);
                });

            }).CreateClient(new WebApplicationFactoryClientOptions());

            var request = new HttpRequestMessage(HttpMethod.Put, $"push-contacts/{deviceToken}/visitor-guid")
            {
                Headers = { { "Authorization", $"Bearer {TestApiUsersData.TOKEN_SUPERUSER_EXPIRE_20330518}" } },
                Content = JsonContent.Create(visitorGuid)
            };

            // Act
            var response = await client.SendAsync(request);
            _output.WriteLine(response.GetHeadersAsString());

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task UpdatePushContactVisitorGuid_should_return_internal_server_error_when_service_throw_an_exception()
        {
            // Arrange
            var fixture = new Fixture();

            var deviceToken = fixture.Create<string>();
            var visitorGuid = fixture.Create<string>();

            var pushContactServiceMock = new Mock<IPushContactService>();

            pushContactServiceMock
                .Setup(x => x.UpdateEmailAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ThrowsAsync(new Exception());

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(pushContactServiceMock.Object);
                });

            }).CreateClient(new WebApplicationFactoryClientOptions());

            var request = new HttpRequestMessage(HttpMethod.Put, $"push-contacts/{deviceToken}/visitor-guid")
            {
                Headers = { { "Authorization", $"Bearer {TestApiUsersData.TOKEN_SUPERUSER_EXPIRE_20330518}" } },
                Content = JsonContent.Create(visitorGuid)
            };

            // Act
            var response = await client.SendAsync(request);
            _output.WriteLine(response.GetHeadersAsString());

            // Assert
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        }

        [Theory]
        [InlineData(" ", "")]
        [InlineData("example_device_token", "")]
        [InlineData("example_device_token", " ")]
        [InlineData("example_device_token", null)]
        [InlineData(" ", "example_visitor_guid")]
        public async Task UpdatePushContactVisitorGuid_should_return_bad_request_when_device_token_or_visitor_guid_are_empty_or_hite_space(string deviceToken, string visitorGuid)
        {
            // Arrange
            var fixture = new Fixture();

            var pushContactServiceMock = new Mock<IPushContactService>();
            var messageRepositoryMock = new Mock<IMessageRepository>();
            var webPushEventServiceMock = new Mock<IWebPushEventService>();

            pushContactServiceMock
                .Setup(x => x.UpdateEmailAsync(It.IsAny<string>(), It.IsAny<string>()))
                .Returns(Task.CompletedTask);

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(pushContactServiceMock.Object);
                    services.AddSingleton(messageRepositoryMock.Object);
                    services.AddSingleton(webPushEventServiceMock.Object);
                });

            }).CreateClient(new WebApplicationFactoryClientOptions());

            var request = new HttpRequestMessage(HttpMethod.Put, $"push-contacts/{deviceToken}/visitor-guid")
            {
                Headers = { { "Authorization", $"Bearer {TestApiUsersData.TOKEN_SUPERUSER_EXPIRE_20330518}" } },
                Content = JsonContent.Create(visitorGuid)
            };

            // Act
            var response = await client.SendAsync(request);
            _output.WriteLine(response.GetHeadersAsString());

            // Assert
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Theory]
        [InlineData(TestApiUsersData.TOKEN_EMPTY)]
        [InlineData(TestApiUsersData.TOKEN_BROKEN)]
        public async Task GetBy_should_return_unauthorized_when_token_is_not_valid(string token)
        {
            // Arrange
            var client = _factory.CreateClient(new WebApplicationFactoryClientOptions());

            var fixture = new Fixture();
            var domain = fixture.Create<string>();

            var request = new HttpRequestMessage(HttpMethod.Get, $"push-contacts?domain={domain}")
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
        public async Task GetBy_should_return_unauthorized_when_token_is_a_expired_superuser_token(string token)
        {
            // Arrange
            var client = _factory.CreateClient(new WebApplicationFactoryClientOptions());

            var fixture = new Fixture();
            var domain = fixture.Create<string>();

            var request = new HttpRequestMessage(HttpMethod.Get, $"push-contacts?domain={domain}")
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
        public async Task GetBy_should_require_a_valid_token_with_isSU_flag(string token)
        {
            // Arrange
            var client = _factory.CreateClient(new WebApplicationFactoryClientOptions());

            var fixture = new Fixture();
            var domain = fixture.Create<string>();

            var request = new HttpRequestMessage(HttpMethod.Get, $"push-contacts?domain={domain}")
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
        public async Task GetBy_should_return_unauthorized_when_authorization_header_is_empty()
        {
            // Arrange
            var client = _factory.CreateClient(new WebApplicationFactoryClientOptions());

            var fixture = new Fixture();
            var domain = fixture.Create<string>();

            var request = new HttpRequestMessage(HttpMethod.Get, $"push-contacts?domain={domain}");

            // Act
            var response = await client.SendAsync(request);
            _output.WriteLine(response.GetHeadersAsString());

            // Assert
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task GetBy_should_return_push_contacts_that_service_get_method_return()
        {
            // Arrange
            var fixture = new Fixture();

            var pushContacts = fixture.CreateMany<PushContactModel>(10);

            var pushContactServiceMock = new Mock<IPushContactService>();
            var messageRepositoryMock = new Mock<IMessageRepository>();
            var webPushEventServiceMock = new Mock<IWebPushEventService>();

            pushContactServiceMock
                .Setup(x => x.GetAsync(It.IsAny<PushContactFilter>()))
                .ReturnsAsync(pushContacts);

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(pushContactServiceMock.Object);
                    services.AddSingleton(messageRepositoryMock.Object);
                    services.AddSingleton(webPushEventServiceMock.Object);
                });

            }).CreateClient(new WebApplicationFactoryClientOptions());

            var domain = fixture.Create<string>();
            var email = fixture.Create<string>();

            var request = new HttpRequestMessage(HttpMethod.Get, $"push-contacts?domain={domain}&email={email}")
            {
                Headers = { { "Authorization", $"Bearer {TestApiUsersData.TOKEN_SUPERUSER_EXPIRE_20330518}" } }
            };

            // Act
            var response = await client.SendAsync(request);
            _output.WriteLine(response.GetHeadersAsString());

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var responseAsString = await response.Content.ReadAsStringAsync();
            var pushContactsResponse = JsonSerializer.Deserialize<IEnumerable<PushContactModel>>
                (responseAsString, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            Assert.Equal(pushContacts.Count(), pushContactsResponse.Count());

            var pushContactsEnumerator = pushContacts.GetEnumerator();
            var pushContactsResponseEnumerator = pushContactsResponse.GetEnumerator();

            while (pushContactsEnumerator.MoveNext() && pushContactsResponseEnumerator.MoveNext())
            {
                Assert.True(pushContactsEnumerator.Current.Domain == pushContactsResponseEnumerator.Current.Domain);
                Assert.True(pushContactsEnumerator.Current.DeviceToken == pushContactsResponseEnumerator.Current.DeviceToken);
                Assert.True(pushContactsEnumerator.Current.Email == pushContactsResponseEnumerator.Current.Email);
            }
        }

        [Fact]
        public async Task GetBy_should_return_not_found_when_service_get_method_return_a_empty_push_contacts_collection()
        {
            // Arrange
            var fixture = new Fixture();

            var pushContacts = Enumerable.Empty<PushContactModel>();

            var pushContactServiceMock = new Mock<IPushContactService>();
            var messageRepositoryMock = new Mock<IMessageRepository>();
            var webPushEventServiceMock = new Mock<IWebPushEventService>();

            pushContactServiceMock
                .Setup(x => x.GetAsync(It.IsAny<PushContactFilter>()))
                .ReturnsAsync(pushContacts);

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(pushContactServiceMock.Object);
                    services.AddSingleton(messageRepositoryMock.Object);
                    services.AddSingleton(webPushEventServiceMock.Object);
                });

            }).CreateClient(new WebApplicationFactoryClientOptions());

            var domain = fixture.Create<string>();
            var email = fixture.Create<string>();

            var request = new HttpRequestMessage(HttpMethod.Get, $"push-contacts?domain={domain}&email={email}")
            {
                Headers = { { "Authorization", $"Bearer {TestApiUsersData.TOKEN_SUPERUSER_EXPIRE_20330518}" } }
            };

            // Act
            var response = await client.SendAsync(request);
            _output.WriteLine(response.GetHeadersAsString());

            // Assert
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Fact]
        public async Task GetBy_should_return_not_found_when_service_get_method_return_null()
        {
            // Arrange
            var fixture = new Fixture();

            IEnumerable<PushContactModel> pushContacts = null;

            var pushContactServiceMock = new Mock<IPushContactService>();
            var messageRepositoryMock = new Mock<IMessageRepository>();
            var webPushEventServiceMock = new Mock<IWebPushEventService>();

            pushContactServiceMock
                .Setup(x => x.GetAsync(It.IsAny<PushContactFilter>()))
                .ReturnsAsync(pushContacts);

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(pushContactServiceMock.Object);
                    services.AddSingleton(messageRepositoryMock.Object);
                    services.AddSingleton(webPushEventServiceMock.Object);
                });

            }).CreateClient(new WebApplicationFactoryClientOptions());

            var domain = fixture.Create<string>();
            var email = fixture.Create<string>();

            var request = new HttpRequestMessage(HttpMethod.Get, $"push-contacts?domain={domain}&email={email}")
            {
                Headers = { { "Authorization", $"Bearer {TestApiUsersData.TOKEN_SUPERUSER_EXPIRE_20330518}" } }
            };

            // Act
            var response = await client.SendAsync(request);
            _output.WriteLine(response.GetHeadersAsString());

            // Assert
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Fact]
        public async Task GetBy_should_return_bad_request_when_domain_param_is_not_in_query_string()
        {
            // Arrange
            var pushContactServiceMock = new Mock<IPushContactService>();
            var messageRepositoryMock = new Mock<IMessageRepository>();
            var webPushEventServiceMock = new Mock<IWebPushEventService>();

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(pushContactServiceMock.Object);
                    services.AddSingleton(messageRepositoryMock.Object);
                    services.AddSingleton(webPushEventServiceMock.Object);
                });

            }).CreateClient(new WebApplicationFactoryClientOptions());

            var request = new HttpRequestMessage(HttpMethod.Get, $"push-contacts")
            {
                Headers = { { "Authorization", $"Bearer {TestApiUsersData.TOKEN_SUPERUSER_EXPIRE_20330518}" } }
            };

            // Act
            var response = await client.SendAsync(request);
            _output.WriteLine(response.GetHeadersAsString());

            // Assert
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Theory(Skip = "Endpoint removed")]
        [InlineData(TestApiUsersData.TOKEN_EMPTY)]
        [InlineData(TestApiUsersData.TOKEN_BROKEN)]
        public async Task BulkDelete_should_return_unauthorized_when_token_is_not_valid(string token)
        {
            // Arrange
            var client = _factory.CreateClient(new WebApplicationFactoryClientOptions());

            var request = new HttpRequestMessage(HttpMethod.Delete, "push-contacts/_bulk")
            {
                Headers = { { "Authorization", $"Bearer {token}" } }
            };

            // Act
            var response = await client.SendAsync(request);
            _output.WriteLine(response.GetHeadersAsString());

            // Assert
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Theory(Skip = "Endpoint removed")]
        [InlineData(TestApiUsersData.TOKEN_SUPERUSER_EXPIRE_20010908)]
        public async Task BulkDelete_should_return_unauthorized_when_token_is_a_expired_superuser_token(string token)
        {
            // Arrange
            var client = _factory.CreateClient(new WebApplicationFactoryClientOptions());

            var request = new HttpRequestMessage(HttpMethod.Delete, "push-contacts/_bulk")
            {
                Headers = { { "Authorization", $"Bearer {token}" } }
            };

            // Act
            var response = await client.SendAsync(request);
            _output.WriteLine(response.GetHeadersAsString());

            // Assert
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Theory(Skip = "Endpoint removed")]
        [InlineData(TestApiUsersData.TOKEN_SUPERUSER_NOTDEFINED_EXPIRE_20330518)]
        [InlineData(TestApiUsersData.TOKEN_SUPERUSER_FALSE_EXPIRE_20330518)]
        [InlineData(TestApiUsersData.TOKEN_ACCOUNT_123_TEST1_AT_TEST_DOT_COM_EXPIRE_20330518)]
        public async Task BulkDelete_should_require_a_valid_token_with_isSU_flag(string token)
        {
            // Arrange
            var client = _factory.CreateClient(new WebApplicationFactoryClientOptions());

            var request = new HttpRequestMessage(HttpMethod.Delete, "push-contacts/_bulk")
            {
                Headers = { { "Authorization", $"Bearer {token}" } }
            };

            // Act
            var response = await client.SendAsync(request);
            _output.WriteLine(response.GetHeadersAsString());

            // Assert
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact(Skip = "Endpoint removed")]
        public async Task BulkDelete_should_return_unauthorized_when_authorization_header_is_empty()
        {
            // Arrange
            var client = _factory.CreateClient(new WebApplicationFactoryClientOptions());

            var request = new HttpRequestMessage(HttpMethod.Delete, "push-contacts/_bulk");

            // Act
            var response = await client.SendAsync(request);
            _output.WriteLine(response.GetHeadersAsString());

            // Assert
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact(Skip = "Endpoint removed")]
        public async Task BulkDelete_should_return_ok_and_deleted_count_when_service_does_not_throw_an_exception()
        {
            // Arrange
            var fixture = new Fixture();

            var deletedCount = fixture.Create<int>();

            var pushContactServiceMock = new Mock<IPushContactService>();

            pushContactServiceMock
                .Setup(x => x.DeleteByDeviceTokenAsync(It.IsAny<IEnumerable<string>>()))
                .ReturnsAsync(deletedCount);

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(pushContactServiceMock.Object);
                });

            }).CreateClient(new WebApplicationFactoryClientOptions());

            var request = new HttpRequestMessage(HttpMethod.Delete, "push-contacts/_bulk")
            {
                Headers = { { "Authorization", $"Bearer {TestApiUsersData.TOKEN_SUPERUSER_EXPIRE_20330518}" } },
                Content = JsonContent.Create(fixture.Create<IEnumerable<string>>())
            };

            // Act
            var response = await client.SendAsync(request);
            _output.WriteLine(response.GetHeadersAsString());

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var responseAsString = await response.Content.ReadAsStringAsync();
            Assert.Equal(deletedCount.ToString(), responseAsString);
        }

        [Fact(Skip = "Endpoint removed")]
        public async Task BulkDelete_should_return_internal_server_error_when_service_throw_an_exception()
        {
            // Arrange
            var fixture = new Fixture();

            var pushContactServiceMock = new Mock<IPushContactService>();

            pushContactServiceMock
                .Setup(x => x.DeleteByDeviceTokenAsync(It.IsAny<IEnumerable<string>>()))
                .ThrowsAsync(new Exception());

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(pushContactServiceMock.Object);
                });

            }).CreateClient(new WebApplicationFactoryClientOptions());

            var request = new HttpRequestMessage(HttpMethod.Delete, "push-contacts/_bulk")
            {
                Headers = { { "Authorization", $"Bearer {TestApiUsersData.TOKEN_SUPERUSER_EXPIRE_20330518}" } },
                Content = JsonContent.Create(fixture.Create<IEnumerable<string>>())
            };

            // Act
            var response = await client.SendAsync(request);
            _output.WriteLine(response.GetHeadersAsString());

            // Assert
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        }

        [Theory]
        [InlineData(TestApiUsersData.TOKEN_EMPTY)]
        [InlineData(TestApiUsersData.TOKEN_BROKEN)]
        public async Task Message_should_return_unauthorized_when_token_is_not_valid(string token)
        {
            // Arrange
            var client = _factory.CreateClient(new WebApplicationFactoryClientOptions());

            var fixture = new Fixture();
            var domain = fixture.Create<string>();

            var request = new HttpRequestMessage(HttpMethod.Post, $"push-contacts/{domain}/message")
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
        public async Task Message_should_return_unauthorized_when_token_is_a_expired_superuser_token(string token)
        {
            // Arrange
            var client = _factory.CreateClient(new WebApplicationFactoryClientOptions());

            var fixture = new Fixture();
            var domain = fixture.Create<string>();

            var request = new HttpRequestMessage(HttpMethod.Post, $"push-contacts/{domain}/message")
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
        public async Task Message_should_require_a_valid_token_with_isSU_flag(string token)
        {
            // Arrange
            var client = _factory.CreateClient(new WebApplicationFactoryClientOptions());

            var fixture = new Fixture();
            var domain = fixture.Create<string>();

            var request = new HttpRequestMessage(HttpMethod.Post, $"push-contacts/{domain}/message")
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
        public async Task Message_should_return_unauthorized_when_authorization_header_is_empty()
        {
            // Arrange
            var client = _factory.CreateClient(new WebApplicationFactoryClientOptions());

            var fixture = new Fixture();
            var domain = fixture.Create<string>();

            var request = new HttpRequestMessage(HttpMethod.Post, $"push-contacts/{domain}/message");

            // Act
            var response = await client.SendAsync(request);
            _output.WriteLine(response.GetHeadersAsString());

            // Assert
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Theory]
        [InlineData(null, "some body")]
        [InlineData("some title", null)]
        [InlineData(null, null)]
        [InlineData("", "some body")]
        [InlineData("some title", "")]
        [InlineData("", "")]
        public async Task Message_should_return_bad_request_when_title_or_body_are_missing(string title, string body)
        {
            // Arrange
            var pushContactServiceMock = new Mock<IPushContactService>();
            var messageRepositoryMock = new Mock<IMessageRepository>();
            var webPushEventServiceMock = new Mock<IWebPushEventService>();

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(pushContactServiceMock.Object);
                    services.AddSingleton(messageRepositoryMock.Object);
                    services.AddSingleton(webPushEventServiceMock.Object);
                });

            }).CreateClient(new WebApplicationFactoryClientOptions());

            var fixture = new Fixture();
            var domain = fixture.Create<string>();
            var message = new Message
            {
                Title = title,
                Body = body,
                OnClickLink = fixture.Create<string>()
            };

            var request = new HttpRequestMessage(HttpMethod.Post, $"push-contacts/{domain}/message")
            {
                Headers = { { "Authorization", $"Bearer {TestApiUsersData.TOKEN_SUPERUSER_EXPIRE_20330518}" } },
                Content = JsonContent.Create(message)
            };

            // Act
            var response = await client.SendAsync(request);
            _output.WriteLine(response.GetHeadersAsString());

            // Assert
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task Message_should_does_not_call_DeleteByDeviceTokenAsync_when_all_device_tokens_returned_by_message_sender_are_valid()
        {
            // Arrange
            var fixture = new Fixture();

            var pushContactServiceMock = new Mock<IPushContactService>();

            var sendMessageResult = new SendMessageResult
            {
                SendMessageTargetResult = fixture.CreateMany<SendMessageTargetResult>(10)
            };
            sendMessageResult.SendMessageTargetResult.ToList().ForEach(x => x.IsValidTargetDeviceToken = true);

            var messageSenderMock = new Mock<IMessageSender>();
            messageSenderMock
                .Setup(x => x.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<string>(), It.IsAny<string>(), null))
                .ReturnsAsync(sendMessageResult);

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(pushContactServiceMock.Object);
                    services.AddSingleton(messageSenderMock.Object);
                });
            }).CreateClient(new WebApplicationFactoryClientOptions());

            var domain = fixture.Create<string>();
            var message = new Message
            {
                Title = fixture.Create<string>(),
                Body = fixture.Create<string>(),
                OnClickLink = fixture.Create<string>()
            };

            var request = new HttpRequestMessage(HttpMethod.Post, $"push-contacts/{domain}/message")
            {
                Headers = { { "Authorization", $"Bearer {TestApiUsersData.TOKEN_SUPERUSER_EXPIRE_20330518}" } },
                Content = JsonContent.Create(message)
            };

            // Act
            var response = await client.SendAsync(request);

            // Assert
            pushContactServiceMock
                .Verify(x => x.DeleteByDeviceTokenAsync(It.IsAny<IEnumerable<string>>()), Times.Never());
        }

        [Fact]
        public async Task Message_should_does_not_call_DeleteByDeviceTokenAsync_when_message_sender_returned_an_empty_target_result_collection()
        {
            // Arrange
            var fixture = new Fixture();

            var pushContactServiceMock = new Mock<IPushContactService>();

            var sendMessageResult = new SendMessageResult
            {
                SendMessageTargetResult = new List<SendMessageTargetResult>()
            };

            var messageSenderMock = new Mock<IMessageSender>();
            messageSenderMock
                .Setup(x => x.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<string>(), It.IsAny<string>(), null))
                .ReturnsAsync(sendMessageResult);

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(pushContactServiceMock.Object);
                    services.AddSingleton(messageSenderMock.Object);
                });
            }).CreateClient(new WebApplicationFactoryClientOptions());

            var domain = fixture.Create<string>();
            var message = new Message
            {
                Title = fixture.Create<string>(),
                Body = fixture.Create<string>(),
                OnClickLink = fixture.Create<string>()
            };

            var request = new HttpRequestMessage(HttpMethod.Post, $"push-contacts/{domain}/message")
            {
                Headers = { { "Authorization", $"Bearer {TestApiUsersData.TOKEN_SUPERUSER_EXPIRE_20330518}" } },
                Content = JsonContent.Create(message)
            };

            // Act
            var response = await client.SendAsync(request);

            // Assert
            pushContactServiceMock
                .Verify(x => x.DeleteByDeviceTokenAsync(It.IsAny<IEnumerable<string>>()), Times.Never());
        }

        [Fact]
        public async Task Message_should_does_not_call_DeleteByDeviceTokenAsync_when_message_sender_returned_null_as_target_result_collection()
        {
            // Arrange
            var fixture = new Fixture();

            var pushContactServiceMock = new Mock<IPushContactService>();

            var sendMessageResult = new SendMessageResult
            {
                SendMessageTargetResult = null
            };

            var messageSenderMock = new Mock<IMessageSender>();
            messageSenderMock
                .Setup(x => x.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<string>(), It.IsAny<string>(), null))
                .ReturnsAsync(sendMessageResult);

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(pushContactServiceMock.Object);
                    services.AddSingleton(messageSenderMock.Object);
                });
            }).CreateClient(new WebApplicationFactoryClientOptions());

            var domain = fixture.Create<string>();
            var message = new Message
            {
                Title = fixture.Create<string>(),
                Body = fixture.Create<string>(),
                OnClickLink = fixture.Create<string>()
            };

            var request = new HttpRequestMessage(HttpMethod.Post, $"push-contacts/{domain}/message")
            {
                Headers = { { "Authorization", $"Bearer {TestApiUsersData.TOKEN_SUPERUSER_EXPIRE_20330518}" } },
                Content = JsonContent.Create(message)
            };

            // Act
            var response = await client.SendAsync(request);

            // Assert
            pushContactServiceMock
                .Verify(x => x.DeleteByDeviceTokenAsync(It.IsAny<IEnumerable<string>>()), Times.Never());
        }

        [Fact]
        public async Task
            Message_should_does_not_call_AddHistoryEventsAsync_when_message_sender_returned_a_empty_target_result_collection()
        {
            // Arrange
            var fixture = new Fixture();

            var pushContactServiceMock = new Mock<IPushContactService>();

            var sendMessageResult = new SendMessageResult
            {
                SendMessageTargetResult = Enumerable.Empty<SendMessageTargetResult>()
            };

            var messageSenderMock = new Mock<IMessageSender>();
            messageSenderMock
                .Setup(x => x.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<string>(), It.IsAny<string>(), null))
                .ReturnsAsync(sendMessageResult);

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(pushContactServiceMock.Object);
                    services.AddSingleton(messageSenderMock.Object);
                });
            }).CreateClient(new WebApplicationFactoryClientOptions());

            var domain = fixture.Create<string>();
            var message = new Message
            {
                Title = fixture.Create<string>(),
                Body = fixture.Create<string>(),
                OnClickLink = fixture.Create<string>()
            };

            var request = new HttpRequestMessage(HttpMethod.Post, $"push-contacts/{domain}/message")
            {
                Headers = { { "Authorization", $"Bearer {TestApiUsersData.TOKEN_SUPERUSER_EXPIRE_20330518}" } },
                Content = JsonContent.Create(message)
            };

            // Act
            var response = await client.SendAsync(request);

            // Assert
            pushContactServiceMock
                .Verify(x => x.AddHistoryEventsAsync(It.IsAny<IEnumerable<PushContactHistoryEvent>>()), Times.Never());
        }

        [Fact]
        public async Task
            Message_should_does_not_call_AddHistoryEventsAsync_when_message_sender_returned_a_null_target_result_collection()
        {
            // Arrange
            var fixture = new Fixture();

            var pushContactServiceMock = new Mock<IPushContactService>();

            var sendMessageResult = new SendMessageResult
            {
                SendMessageTargetResult = null
            };

            var messageSenderMock = new Mock<IMessageSender>();
            messageSenderMock
                .Setup(x => x.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<string>(), It.IsAny<string>(), null))
                .ReturnsAsync(sendMessageResult);

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(pushContactServiceMock.Object);
                    services.AddSingleton(messageSenderMock.Object);
                });
            }).CreateClient(new WebApplicationFactoryClientOptions());

            var domain = fixture.Create<string>();
            var message = new Message
            {
                Title = fixture.Create<string>(),
                Body = fixture.Create<string>(),
                OnClickLink = fixture.Create<string>()
            };

            var request = new HttpRequestMessage(HttpMethod.Post, $"push-contacts/{domain}/message")
            {
                Headers = { { "Authorization", $"Bearer {TestApiUsersData.TOKEN_SUPERUSER_EXPIRE_20330518}" } },
                Content = JsonContent.Create(message)
            };

            // Act
            var response = await client.SendAsync(request);

            // Assert
            pushContactServiceMock
                .Verify(x => x.AddHistoryEventsAsync(It.IsAny<IEnumerable<PushContactHistoryEvent>>()), Times.Never());
        }

        [Fact]
        public async Task Message_should_return_ok_and_a_message_result_when_send_message_steps_do_not_throw_a_exception()
        {
            // Arrange
            var fixture = new Fixture();

            var pushContactServiceMock = new Mock<IPushContactService>();
            pushContactServiceMock
            .Setup(x => x.GetAllDeviceTokensByDomainAsync(It.IsAny<string>()))
            .ReturnsAsync(fixture.Create<IEnumerable<string>>());

            pushContactServiceMock
            .Setup(x => x.DeleteByDeviceTokenAsync(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(fixture.Create<int>());

            pushContactServiceMock
            .Setup(x => x.AddHistoryEventsAsync(It.IsAny<IEnumerable<PushContactHistoryEvent>>()))
            .Returns(Task.CompletedTask);

            var messageSenderMock = new Mock<IMessageSender>();
            messageSenderMock
                .Setup(x => x.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<string>(), It.IsAny<string>(), null))
                .ReturnsAsync(fixture.Create<SendMessageResult>());

            var messageRepositoryMock = new Mock<IMessageRepository>();
            var webPushEventServiceMock = new Mock<IWebPushEventService>();

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(pushContactServiceMock.Object);
                    services.AddSingleton(messageSenderMock.Object);
                    services.AddSingleton(messageRepositoryMock.Object);
                    services.AddSingleton(webPushEventServiceMock.Object);
                });
            }).CreateClient(new WebApplicationFactoryClientOptions());

            var domain = fixture.Create<string>();
            var message = new Message
            {
                Title = fixture.Create<string>(),
                Body = fixture.Create<string>(),
                OnClickLink = fixture.Create<string>()
            };

            var request = new HttpRequestMessage(HttpMethod.Post, $"push-contacts/{domain}/message")
            {
                Headers = { { "Authorization", $"Bearer {TestApiUsersData.TOKEN_SUPERUSER_EXPIRE_20330518}" } },
                Content = JsonContent.Create(message)
            };

            // Act
            var response = await client.SendAsync(request);

            // Assert
            var messageResult = await response.Content.ReadFromJsonAsync<MessageResult>();
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        }

        [Fact]
        public async Task Message_should_return_internal_server_error_when_GetAllDeviceTokensByDomainAsync_throw_a_exception()
        {
            // Arrange
            var fixture = new Fixture();

            var pushContactServiceMock = new Mock<IPushContactService>();
            pushContactServiceMock
                .Setup(x => x.GetAllDeviceTokensByDomainAsync(It.IsAny<string>()))
                .ThrowsAsync(new Exception());

            var messageSenderMock = new Mock<IMessageSender>();

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(pushContactServiceMock.Object);
                    services.AddSingleton(messageSenderMock.Object);
                });
            }).CreateClient(new WebApplicationFactoryClientOptions());

            var domain = fixture.Create<string>();
            var message = new Message
            {
                Title = fixture.Create<string>(),
                Body = fixture.Create<string>(),
                OnClickLink = fixture.Create<string>()
            };

            var request = new HttpRequestMessage(HttpMethod.Post, $"push-contacts/{domain}/message")
            {
                Headers = { { "Authorization", $"Bearer {TestApiUsersData.TOKEN_SUPERUSER_EXPIRE_20330518}" } },
                Content = JsonContent.Create(message)
            };

            // Act
            var response = await client.SendAsync(request);

            // Assert
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        }

        [Fact]
        public async Task Message_should_return_internal_server_error_when_SendAsync_throw_a_exception()
        {
            // Arrange
            var fixture = new Fixture();

            var pushContactServiceMock = new Mock<IPushContactService>();

            var messageSenderMock = new Mock<IMessageSender>();
            messageSenderMock
                .Setup(x => x.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<string>(), It.IsAny<string>(), null))
                .ThrowsAsync(new Exception());

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(pushContactServiceMock.Object);
                    services.AddSingleton(messageSenderMock.Object);
                });
            }).CreateClient(new WebApplicationFactoryClientOptions());

            var domain = fixture.Create<string>();
            var message = new Message
            {
                Title = fixture.Create<string>(),
                Body = fixture.Create<string>(),
                OnClickLink = fixture.Create<string>()
            };

            var request = new HttpRequestMessage(HttpMethod.Post, $"push-contacts/{domain}/message")
            {
                Headers = { { "Authorization", $"Bearer {TestApiUsersData.TOKEN_SUPERUSER_EXPIRE_20330518}" } },
                Content = JsonContent.Create(message)
            };

            // Act
            var response = await client.SendAsync(request);

            // Assert
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public async Task Message_should_allow_missing_onClickLink_param(string onClickLink)
        {
            // Arrange
            var pushContactServiceMock = new Mock<IPushContactService>();
            var messageSenderMock = new Mock<IMessageSender>();

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(pushContactServiceMock.Object);
                    services.AddSingleton(messageSenderMock.Object);
                });

            }).CreateClient(new WebApplicationFactoryClientOptions());

            var fixture = new Fixture();
            var domain = fixture.Create<string>();
            var message = new Message
            {
                Title = fixture.Create<string>(),
                Body = fixture.Create<string>(),
                OnClickLink = onClickLink
            };

            var request = new HttpRequestMessage(HttpMethod.Post, $"push-contacts/{domain}/message")
            {
                Headers = { { "Authorization", $"Bearer {TestApiUsersData.TOKEN_SUPERUSER_EXPIRE_20330518}" } },
                Content = JsonContent.Create(message)
            };

            // Act
            var response = await client.SendAsync(request);
            _output.WriteLine(response.GetHeadersAsString());

            // Assert
            Assert.NotEqual(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Theory]
        [InlineData(TestApiUsersData.TOKEN_EMPTY)]
        [InlineData(TestApiUsersData.TOKEN_BROKEN)]
        public async Task Message_By_Visitor_Guid_should_return_unauthorized_when_token_is_not_valid(string token)
        {
            // Arrange
            var client = _factory.CreateClient(new WebApplicationFactoryClientOptions());

            var fixture = new Fixture();
            var domain = fixture.Create<string>();
            var visitorGuid = fixture.Create<string>();

            var request = new HttpRequestMessage(HttpMethod.Post, $"push-contacts/{domain}/{visitorGuid}/message")
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
        public async Task Message_By_Visitor_Guid_should_return_unauthorized_when_token_is_a_expired_superuser_token(string token)
        {
            // Arrange
            var client = _factory.CreateClient(new WebApplicationFactoryClientOptions());

            var fixture = new Fixture();
            var domain = fixture.Create<string>();
            var visitorGuid = fixture.Create<string>();

            var request = new HttpRequestMessage(HttpMethod.Post, $"push-contacts/{domain}/{visitorGuid}/message")
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
        public async Task Message_By_Visitor_Guid_should_require_a_valid_token_with_isSU_flag(string token)
        {
            // Arrange
            var client = _factory.CreateClient(new WebApplicationFactoryClientOptions());

            var fixture = new Fixture();
            var domain = fixture.Create<string>();
            var visitorGuid = fixture.Create<string>();

            var request = new HttpRequestMessage(HttpMethod.Post, $"push-contacts/{domain}/{visitorGuid}/message")
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
        public async Task Message_By_Visitor_Guid_should_return_unauthorized_when_authorization_header_is_empty()
        {
            // Arrange
            var client = _factory.CreateClient(new WebApplicationFactoryClientOptions());

            var fixture = new Fixture();
            var domain = fixture.Create<string>();
            var visitorGuid = fixture.Create<string>();

            var request = new HttpRequestMessage(HttpMethod.Post, $"push-contacts/{domain}/{visitorGuid}/message");

            // Act
            var response = await client.SendAsync(request);
            _output.WriteLine(response.GetHeadersAsString());

            // Assert
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Theory]
        [InlineData(null, "some body")]
        [InlineData("some title", null)]
        [InlineData(null, null)]
        [InlineData("", "some body")]
        [InlineData("some title", "")]
        [InlineData("", "")]
        public async Task Message_By_Visitor_Guid_should_return_bad_request_when_title_or_body_are_missing(string title, string body)
        {
            // Arrange
            var pushContactServiceMock = new Mock<IPushContactService>();
            var messageRepositoryMock = new Mock<IMessageRepository>();
            var webPushEventServiceMock = new Mock<IWebPushEventService>();

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(pushContactServiceMock.Object);
                    services.AddSingleton(messageRepositoryMock.Object);
                    services.AddSingleton(webPushEventServiceMock.Object);
                });

            }).CreateClient(new WebApplicationFactoryClientOptions());

            var fixture = new Fixture();
            var domain = fixture.Create<string>();
            var message = new Message
            {
                Title = title,
                Body = body,
                OnClickLink = fixture.Create<string>()
            };
            var visitorGuid = fixture.Create<string>();

            var request = new HttpRequestMessage(HttpMethod.Post, $"push-contacts/{domain}/{visitorGuid}/message")
            {
                Headers = { { "Authorization", $"Bearer {TestApiUsersData.TOKEN_SUPERUSER_EXPIRE_20330518}" } },
                Content = JsonContent.Create(message)
            };

            // Act
            var response = await client.SendAsync(request);
            _output.WriteLine(response.GetHeadersAsString());

            // Assert
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task Message_By_Visitor_Guid_should_does_not_call_DeleteByDeviceTokenAsync_when_all_device_tokens_returned_by_message_sender_are_valid()
        {
            // Arrange
            var fixture = new Fixture();

            var pushContactServiceMock = new Mock<IPushContactService>();

            var sendMessageResult = new SendMessageResult
            {
                SendMessageTargetResult = fixture.CreateMany<SendMessageTargetResult>(10)
            };
            sendMessageResult.SendMessageTargetResult.ToList().ForEach(x => x.IsValidTargetDeviceToken = true);

            var messageSenderMock = new Mock<IMessageSender>();
            messageSenderMock
                .Setup(x => x.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<string>(), It.IsAny<string>(), null))
                .ReturnsAsync(sendMessageResult);

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(pushContactServiceMock.Object);
                    services.AddSingleton(messageSenderMock.Object);
                });
            }).CreateClient(new WebApplicationFactoryClientOptions());

            var domain = fixture.Create<string>();
            var message = new Message
            {
                Title = fixture.Create<string>(),
                Body = fixture.Create<string>(),
                OnClickLink = fixture.Create<string>()
            };
            var visitorGuid = fixture.Create<string>();

            var request = new HttpRequestMessage(HttpMethod.Post, $"push-contacts/{domain}/{visitorGuid}/message")
            {
                Headers = { { "Authorization", $"Bearer {TestApiUsersData.TOKEN_SUPERUSER_EXPIRE_20330518}" } },
                Content = JsonContent.Create(message)
            };

            // Act
            var response = await client.SendAsync(request);

            // Assert
            pushContactServiceMock
                .Verify(x => x.DeleteByDeviceTokenAsync(It.IsAny<IEnumerable<string>>()), Times.Never());
        }

        [Fact]
        public async Task Message_By_Visitor_Guid_should_does_not_call_DeleteByDeviceTokenAsync_when_message_sender_returned_an_empty_target_result_collection()
        {
            // Arrange
            var fixture = new Fixture();

            var pushContactServiceMock = new Mock<IPushContactService>();

            var sendMessageResult = new SendMessageResult
            {
                SendMessageTargetResult = new List<SendMessageTargetResult>()
            };

            var messageSenderMock = new Mock<IMessageSender>();
            messageSenderMock
                .Setup(x => x.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<string>(), It.IsAny<string>(), null))
                .ReturnsAsync(sendMessageResult);

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(pushContactServiceMock.Object);
                    services.AddSingleton(messageSenderMock.Object);
                });
            }).CreateClient(new WebApplicationFactoryClientOptions());

            var domain = fixture.Create<string>();
            var message = new Message
            {
                Title = fixture.Create<string>(),
                Body = fixture.Create<string>(),
                OnClickLink = fixture.Create<string>()
            };
            var visitorGuid = fixture.Create<string>();

            var request = new HttpRequestMessage(HttpMethod.Post, $"push-contacts/{domain}/{visitorGuid}/message")
            {
                Headers = { { "Authorization", $"Bearer {TestApiUsersData.TOKEN_SUPERUSER_EXPIRE_20330518}" } },
                Content = JsonContent.Create(message)
            };

            // Act
            var response = await client.SendAsync(request);

            // Assert
            pushContactServiceMock
                .Verify(x => x.DeleteByDeviceTokenAsync(It.IsAny<IEnumerable<string>>()), Times.Never());
        }

        [Fact]
        public async Task Message_By_Visitor_Guid_should_does_not_call_DeleteByDeviceTokenAsync_when_message_sender_returned_null_as_target_result_collection()
        {
            // Arrange
            var fixture = new Fixture();

            var pushContactServiceMock = new Mock<IPushContactService>();

            var sendMessageResult = new SendMessageResult
            {
                SendMessageTargetResult = null
            };

            var messageSenderMock = new Mock<IMessageSender>();
            messageSenderMock
                .Setup(x => x.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<string>(), It.IsAny<string>(), null))
                .ReturnsAsync(sendMessageResult);

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(pushContactServiceMock.Object);
                    services.AddSingleton(messageSenderMock.Object);
                });
            }).CreateClient(new WebApplicationFactoryClientOptions());

            var domain = fixture.Create<string>();
            var message = new Message
            {
                Title = fixture.Create<string>(),
                Body = fixture.Create<string>(),
                OnClickLink = fixture.Create<string>()
            };
            var visitorGuid = fixture.Create<string>();

            var request = new HttpRequestMessage(HttpMethod.Post, $"push-contacts/{domain}/{visitorGuid}/message")
            {
                Headers = { { "Authorization", $"Bearer {TestApiUsersData.TOKEN_SUPERUSER_EXPIRE_20330518}" } },
                Content = JsonContent.Create(message)
            };

            // Act
            var response = await client.SendAsync(request);

            // Assert
            pushContactServiceMock
                .Verify(x => x.DeleteByDeviceTokenAsync(It.IsAny<IEnumerable<string>>()), Times.Never());
        }

        [Fact]
        public async Task Message_By_Visitor_Guid_should_does_not_call_AddHistoryEventsAsync_when_message_sender_returned_a_empty_target_result_collection()
        {
            // Arrange
            var fixture = new Fixture();

            var pushContactServiceMock = new Mock<IPushContactService>();

            var sendMessageResult = new SendMessageResult
            {
                SendMessageTargetResult = Enumerable.Empty<SendMessageTargetResult>()
            };

            var messageSenderMock = new Mock<IMessageSender>();
            messageSenderMock
                .Setup(x => x.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<string>(), It.IsAny<string>(), null))
                .ReturnsAsync(sendMessageResult);

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(pushContactServiceMock.Object);
                    services.AddSingleton(messageSenderMock.Object);
                });
            }).CreateClient(new WebApplicationFactoryClientOptions());

            var domain = fixture.Create<string>();
            var message = new Message
            {
                Title = fixture.Create<string>(),
                Body = fixture.Create<string>(),
                OnClickLink = fixture.Create<string>()
            };
            var visitorGuid = fixture.Create<string>();

            var request = new HttpRequestMessage(HttpMethod.Post, $"push-contacts/{domain}/{visitorGuid}/message")
            {
                Headers = { { "Authorization", $"Bearer {TestApiUsersData.TOKEN_SUPERUSER_EXPIRE_20330518}" } },
                Content = JsonContent.Create(message)
            };

            // Act
            var response = await client.SendAsync(request);

            // Assert
            pushContactServiceMock
                .Verify(x => x.AddHistoryEventsAsync(It.IsAny<IEnumerable<PushContactHistoryEvent>>()), Times.Never());
        }

        [Fact]
        public async Task Message_By_Visitor_Guid_should_does_not_call_AddHistoryEventsAsync_when_message_sender_returned_a_null_target_result_collection()
        {
            // Arrange
            var fixture = new Fixture();

            var pushContactServiceMock = new Mock<IPushContactService>();

            var sendMessageResult = new SendMessageResult
            {
                SendMessageTargetResult = null
            };

            var messageSenderMock = new Mock<IMessageSender>();
            messageSenderMock
                .Setup(x => x.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<string>(), It.IsAny<string>(), null))
                .ReturnsAsync(sendMessageResult);

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(pushContactServiceMock.Object);
                    services.AddSingleton(messageSenderMock.Object);
                });
            }).CreateClient(new WebApplicationFactoryClientOptions());

            var domain = fixture.Create<string>();
            var message = new Message
            {
                Title = fixture.Create<string>(),
                Body = fixture.Create<string>(),
                OnClickLink = fixture.Create<string>()
            };
            var visitorGuid = fixture.Create<string>();

            var request = new HttpRequestMessage(HttpMethod.Post, $"push-contacts/{domain}/{visitorGuid}/message")
            {
                Headers = { { "Authorization", $"Bearer {TestApiUsersData.TOKEN_SUPERUSER_EXPIRE_20330518}" } },
                Content = JsonContent.Create(message)
            };

            // Act
            var response = await client.SendAsync(request);

            // Assert
            pushContactServiceMock
                .Verify(x => x.AddHistoryEventsAsync(It.IsAny<IEnumerable<PushContactHistoryEvent>>()), Times.Never());
        }

        [Fact]
        public async Task Message_By_Visitor_Guid_should_call_AddHistoryEventsAsync_with_all_target_device_tokens_returned_by_message_sender()
        {
            // Arrange
            var fixture = new Fixture();

            var pushContactServiceMock = new Mock<IPushContactService>();
            var messageRepositoryMock = new Mock<IMessageRepository>();
            var webPushEventServiceMock = new Mock<IWebPushEventService>();

            var sendMessageResult = new SendMessageResult
            {
                SendMessageTargetResult = fixture.CreateMany<SendMessageTargetResult>(10)
            };

            var messageSenderMock = new Mock<IMessageSender>();
            messageSenderMock
                .Setup(x => x.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<string>(), It.IsAny<string>(), null))
                .ReturnsAsync(sendMessageResult);

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(pushContactServiceMock.Object);
                    services.AddSingleton(messageSenderMock.Object);
                    services.AddSingleton(messageRepositoryMock.Object);
                    services.AddSingleton(webPushEventServiceMock.Object);
                });
            }).CreateClient(new WebApplicationFactoryClientOptions());

            var domain = fixture.Create<string>();
            var message = new Message
            {
                Title = fixture.Create<string>(),
                Body = fixture.Create<string>(),
                OnClickLink = fixture.Create<string>()
            };
            var visitorGuid = fixture.Create<string>();

            var request = new HttpRequestMessage(HttpMethod.Post, $"push-contacts/{domain}/{visitorGuid}/message")
            {
                Headers = { { "Authorization", $"Bearer {TestApiUsersData.TOKEN_SUPERUSER_EXPIRE_20330518}" } },
                Content = JsonContent.Create(message)
            };

            // Act
            var response = await client.SendAsync(request);

            // Assert
            pushContactServiceMock.Verify(x => x.AddHistoryEventsAndMarkDeletedContactsAsync(It.IsAny<Guid>(), sendMessageResult), Times.Once());
        }

        [Fact]
        public async Task Message_By_Visitor_Guid_should_return_ok_and_a_message_result_when_send_message_steps_do_not_throw_a_exception()
        {
            // Arrange
            var fixture = new Fixture();

            var pushContactServiceMock = new Mock<IPushContactService>();
            pushContactServiceMock
            .Setup(x => x.GetAllDeviceTokensByDomainAsync(It.IsAny<string>()))
            .ReturnsAsync(fixture.Create<IEnumerable<string>>());

            pushContactServiceMock
            .Setup(x => x.DeleteByDeviceTokenAsync(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(fixture.Create<int>());

            pushContactServiceMock
            .Setup(x => x.AddHistoryEventsAsync(It.IsAny<IEnumerable<PushContactHistoryEvent>>()))
            .Returns(Task.CompletedTask);

            var messageSenderMock = new Mock<IMessageSender>();
            messageSenderMock
                .Setup(x => x.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<string>(), It.IsAny<string>(), null))
                .ReturnsAsync(fixture.Create<SendMessageResult>());

            var messageRepositoryMock = new Mock<IMessageRepository>();
            var webPushEventServiceMock = new Mock<IWebPushEventService>();

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(pushContactServiceMock.Object);
                    services.AddSingleton(messageSenderMock.Object);
                    services.AddSingleton(messageRepositoryMock.Object);
                    services.AddSingleton(webPushEventServiceMock.Object);
                });
            }).CreateClient(new WebApplicationFactoryClientOptions());

            var domain = fixture.Create<string>();
            var message = new Message
            {
                Title = fixture.Create<string>(),
                Body = fixture.Create<string>(),
                OnClickLink = fixture.Create<string>()
            };
            var visitorGuid = fixture.Create<string>();

            var request = new HttpRequestMessage(HttpMethod.Post, $"push-contacts/{domain}/{visitorGuid}/message")
            {
                Headers = { { "Authorization", $"Bearer {TestApiUsersData.TOKEN_SUPERUSER_EXPIRE_20330518}" } },
                Content = JsonContent.Create(message)
            };

            // Act
            var response = await client.SendAsync(request);

            // Assert
            var messageResult = await response.Content.ReadFromJsonAsync<MessageResult>();
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task Message_By_Visitor_Guid_should_return_internal_server_error_when_GetAllDeviceTokensByDomainAsync_throw_a_exception()
        {
            // Arrange
            var fixture = new Fixture();

            var pushContactServiceMock = new Mock<IPushContactService>();
            pushContactServiceMock
                .Setup(x => x.GetAllDeviceTokensByDomainAsync(It.IsAny<string>()))
                .ThrowsAsync(new Exception());

            var messageSenderMock = new Mock<IMessageSender>();

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(pushContactServiceMock.Object);
                    services.AddSingleton(messageSenderMock.Object);
                });
            }).CreateClient(new WebApplicationFactoryClientOptions());

            var domain = fixture.Create<string>();
            var message = new Message
            {
                Title = fixture.Create<string>(),
                Body = fixture.Create<string>(),
                OnClickLink = fixture.Create<string>()
            };
            var visitorGuid = fixture.Create<string>();

            var request = new HttpRequestMessage(HttpMethod.Post, $"push-contacts/{domain}/{visitorGuid}/message")
            {
                Headers = { { "Authorization", $"Bearer {TestApiUsersData.TOKEN_SUPERUSER_EXPIRE_20330518}" } },
                Content = JsonContent.Create(message)
            };

            // Act
            var response = await client.SendAsync(request);

            // Assert
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        }

        [Fact]
        public async Task Message_By_Visitor_Guid_should_return_internal_server_error_when_SendAsync_throw_a_exception()
        {
            // Arrange
            var fixture = new Fixture();

            var pushContactServiceMock = new Mock<IPushContactService>();

            var messageSenderMock = new Mock<IMessageSender>();
            messageSenderMock
                .Setup(x => x.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<string>(), It.IsAny<string>(), null))
                .ThrowsAsync(new Exception());

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(pushContactServiceMock.Object);
                    services.AddSingleton(messageSenderMock.Object);
                });
            }).CreateClient(new WebApplicationFactoryClientOptions());

            var domain = fixture.Create<string>();
            var message = new Message
            {
                Title = fixture.Create<string>(),
                Body = fixture.Create<string>(),
                OnClickLink = fixture.Create<string>()
            };
            var visitorGuid = fixture.Create<string>();

            var request = new HttpRequestMessage(HttpMethod.Post, $"push-contacts/{domain}/{visitorGuid}/message")
            {
                Headers = { { "Authorization", $"Bearer {TestApiUsersData.TOKEN_SUPERUSER_EXPIRE_20330518}" } },
                Content = JsonContent.Create(message)
            };

            // Act
            var response = await client.SendAsync(request);

            // Assert
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public async Task Message_By_Visitor_Guid_should_allow_missing_onClickLink_param(string onClickLink)
        {
            // Arrange
            var pushContactServiceMock = new Mock<IPushContactService>();
            var messageSenderMock = new Mock<IMessageSender>();

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(pushContactServiceMock.Object);
                    services.AddSingleton(messageSenderMock.Object);
                });

            }).CreateClient(new WebApplicationFactoryClientOptions());

            var fixture = new Fixture();
            var domain = fixture.Create<string>();
            var message = new Message
            {
                Title = fixture.Create<string>(),
                Body = fixture.Create<string>(),
                OnClickLink = onClickLink
            };
            var visitorGuid = fixture.Create<string>();

            var request = new HttpRequestMessage(HttpMethod.Post, $"push-contacts/{domain}/{visitorGuid}/message")
            {
                Headers = { { "Authorization", $"Bearer {TestApiUsersData.TOKEN_SUPERUSER_EXPIRE_20330518}" } },
                Content = JsonContent.Create(message)
            };

            // Act
            var response = await client.SendAsync(request);
            _output.WriteLine(response.GetHeadersAsString());

            // Assert
            Assert.NotEqual(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Theory]
        [InlineData(TestApiUsersData.TOKEN_EMPTY)]
        [InlineData(TestApiUsersData.TOKEN_BROKEN)]
        public async Task GetMessages_should_return_unauthorized_when_token_is_not_valid(string token)
        {
            // Arrange
            var client = _factory.CreateClient(new WebApplicationFactoryClientOptions());

            var request = new HttpRequestMessage(HttpMethod.Get, $"push-contacts/messages/delivery-results")
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
        public async Task GetMessages_should_return_unauthorized_when_token_is_a_expired_superuser_token(string token)
        {
            // Arrange
            var client = _factory.CreateClient(new WebApplicationFactoryClientOptions());

            var request = new HttpRequestMessage(HttpMethod.Get, $"push-contacts/messages/delivery-results")
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
        public async Task GetMessages_should_require_a_valid_token_with_isSU_flag(string token)
        {
            // Arrange
            var client = _factory.CreateClient(new WebApplicationFactoryClientOptions());

            var request = new HttpRequestMessage(HttpMethod.Get, $"push-contacts/messages/delivery-results")
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
        public async Task GetMessages_should_return_unauthorized_when_authorization_header_is_empty()
        {
            // Arrange
            var client = _factory.CreateClient(new WebApplicationFactoryClientOptions());

            var request = new HttpRequestMessage(HttpMethod.Get, $"push-contacts/messages/delivery-results");

            // Act
            var response = await client.SendAsync(request);
            _output.WriteLine(response.GetHeadersAsString());

            // Assert
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Theory]
        [InlineData(0, 1, "2022-06-30T20:13:34.729+00:00", "2022-05-25T20:13:34.729+00:00")]
        public async Task GetMessages_should_throw_exception_when_from_are_greater_than_to(int _page, int _per_page, DateTimeOffset _from, DateTimeOffset _to)
        {
            // Arrange
            var pushContactServiceMock = new Mock<IPushContactService>();
            var messageRepositoryMock = new Mock<IMessageRepository>();
            var webPushEventServiceMock = new Mock<IWebPushEventService>();

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(pushContactServiceMock.Object);
                    services.AddSingleton(messageRepositoryMock.Object);
                    services.AddSingleton(webPushEventServiceMock.Object);
                });

            }).CreateClient(new WebApplicationFactoryClientOptions());

            var url = $"push-contacts/messages/delivery-results?page={_page}&per_page={_per_page}&from={_from.ToUniversalTime():yyyy-MM-ddTHH:mm:ss.fffZ}&to={_to.ToUniversalTime():yyyy-MM-ddTHH:mm:ss.fffZ}";
            var request = new HttpRequestMessage(HttpMethod.Get, url)
            {
                Headers = { { "Authorization", $"Bearer {TestApiUsersData.TOKEN_SUPERUSER_EXPIRE_20330518}" } },

            };

            // Act
            var response = await client.SendAsync(request);
            _output.WriteLine(response.GetHeadersAsString());

            // Assert
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Theory]
        [InlineData(-1, 1, "2022-05-25T20:13:34.729+00:00", "2022-05-25T20:13:34.729+00:00")]
        public async Task GetMessages_should_throw_exception_when_page_are_lesser_than_zero(int _page, int _per_page, string _from, string _to)
        {
            // Arrange
            var pushContactServiceMock = new Mock<IPushContactService>();
            var messageRepositoryMock = new Mock<IMessageRepository>();
            var webPushEventServiceMock = new Mock<IWebPushEventService>();

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(pushContactServiceMock.Object);
                    services.AddSingleton(messageRepositoryMock.Object);
                    services.AddSingleton(webPushEventServiceMock.Object);
                });

            }).CreateClient(new WebApplicationFactoryClientOptions());

            var request = new HttpRequestMessage(HttpMethod.Get, $"push-contacts/messages/delivery-results?page={_page}&per_page={_per_page}&from={_from}&to={_to}")
            {
                Headers = { { "Authorization", $"Bearer {TestApiUsersData.TOKEN_SUPERUSER_EXPIRE_20330518}" } },
            };

            // Act
            var response = await client.SendAsync(request);
            _output.WriteLine(response.GetHeadersAsString());

            // Assert
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Theory]
        [InlineData(0, 0, "2020-06-24T20:13:34.729+00:00", "2022-05-25T20:13:34.729+00:00")]
        [InlineData(0, -1, "2020-06-24T20:13:34.729+00:00", "2022-05-25T20:13:34.729+00:00")]
        public async Task GetMessages_should_throw_exception_when_per_page_are_zero_or_lesser(int _page, int _per_page, DateTimeOffset _from, DateTimeOffset _to)
        {
            // Arrange
            var pushContactServiceMock = new Mock<IPushContactService>();
            var messageRepositoryMock = new Mock<IMessageRepository>();
            var webPushEventServiceMock = new Mock<IWebPushEventService>();

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(pushContactServiceMock.Object);
                    services.AddSingleton(messageRepositoryMock.Object);
                    services.AddSingleton(webPushEventServiceMock.Object);
                });

            }).CreateClient(new WebApplicationFactoryClientOptions());

            var request = new HttpRequestMessage(HttpMethod.Get, $"push-contacts/messages/delivery-results?page={_page}&per_page={_per_page}&from={_from}&to={_to}")
            {
                Headers = { { "Authorization", $"Bearer {TestApiUsersData.TOKEN_SUPERUSER_EXPIRE_20330518}" } },
            };

            // Act
            var response = await client.SendAsync(request);
            _output.WriteLine(response.GetHeadersAsString());

            // Assert
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Theory]
        [InlineData(TestApiUsersData.TOKEN_EMPTY)]
        [InlineData(TestApiUsersData.TOKEN_BROKEN)]
        public async Task GetDomains_should_return_unauthorized_when_token_is_not_valid(string token)
        {
            // Arrange
            var client = _factory.CreateClient(new WebApplicationFactoryClientOptions());

            var request = new HttpRequestMessage(HttpMethod.Get, $"push-contacts/domains")
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
        public async Task GetDomains_should_return_unauthorized_when_token_is_a_expired_superuser_token(string token)
        {
            // Arrange
            var client = _factory.CreateClient(new WebApplicationFactoryClientOptions());

            var request = new HttpRequestMessage(HttpMethod.Get, $"push-contacts/domains")
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
        public async Task GetDomains_should_require_a_valid_token_with_isSU_flag(string token)
        {
            // Arrange
            var client = _factory.CreateClient(new WebApplicationFactoryClientOptions());

            var request = new HttpRequestMessage(HttpMethod.Get, $"push-contacts/domains")
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
        public async Task GetDomains_should_return_unauthorized_when_authorization_header_is_empty()
        {
            // Arrange
            var client = _factory.CreateClient(new WebApplicationFactoryClientOptions());

            var request = new HttpRequestMessage(HttpMethod.Get, $"push-contacts/domains");

            // Act
            var response = await client.SendAsync(request);
            _output.WriteLine(response.GetHeadersAsString());

            // Assert
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Theory]
        [InlineData(-1, 1)]
        public async Task GetDomains_should_throw_exception_when_page_are_lesser_than_zero(int _page, int _per_page)
        {
            // Arrange
            var pushContactServiceMock = new Mock<IPushContactService>();
            var messageRepositoryMock = new Mock<IMessageRepository>();
            var webPushEventServiceMock = new Mock<IWebPushEventService>();

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(pushContactServiceMock.Object);
                    services.AddSingleton(messageRepositoryMock.Object);
                    services.AddSingleton(webPushEventServiceMock.Object);
                });

            }).CreateClient(new WebApplicationFactoryClientOptions());

            var request = new HttpRequestMessage(HttpMethod.Get, $"push-contacts/domains?page={_page}&per_page={_per_page}")
            {
                Headers = { { "Authorization", $"Bearer {TestApiUsersData.TOKEN_SUPERUSER_EXPIRE_20330518}" } },
            };

            // Act
            var response = await client.SendAsync(request);
            _output.WriteLine(response.GetHeadersAsString());

            // Assert
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Theory]
        [InlineData(0, 0)]
        [InlineData(0, -1)]
        public async Task GetDomains_should_throw_exception_when_per_page_are_zero_or_lesser(int _page, int _per_page)
        {
            // Arrange
            var pushContactServiceMock = new Mock<IPushContactService>();
            var messageRepositoryMock = new Mock<IMessageRepository>();
            var webPushEventServiceMock = new Mock<IWebPushEventService>();

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(pushContactServiceMock.Object);
                    services.AddSingleton(messageRepositoryMock.Object);
                    services.AddSingleton(webPushEventServiceMock.Object);
                });

            }).CreateClient(new WebApplicationFactoryClientOptions());

            var request = new HttpRequestMessage(HttpMethod.Get, $"push-contacts/domains?page={_page}&per_page={_per_page}")
            {
                Headers = { { "Authorization", $"Bearer {TestApiUsersData.TOKEN_SUPERUSER_EXPIRE_20330518}" } },
            };

            // Act
            var response = await client.SendAsync(request);
            _output.WriteLine(response.GetHeadersAsString());

            // Assert
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Theory]
        [InlineData(TestApiUsersData.TOKEN_EMPTY, "example.com")]
        [InlineData(TestApiUsersData.TOKEN_BROKEN, "example.com")]
        public async Task GetAllVisitorGuidByDomain_should_return_unauthorized_when_token_is_not_valid(string token, string domain)
        {
            // Arrange
            var client = _factory.CreateClient(new WebApplicationFactoryClientOptions());

            var request = new HttpRequestMessage(HttpMethod.Get, $"push-contacts/{domain}/visitor-guids")
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
        [InlineData(TestApiUsersData.TOKEN_SUPERUSER_EXPIRE_20010908, "example.com")]
        public async Task GetAllVisitorGuidByDomain_should_return_unauthorized_when_token_is_a_expired_superuser_token(string token, string domain)
        {
            // Arrange
            var client = _factory.CreateClient(new WebApplicationFactoryClientOptions());

            var request = new HttpRequestMessage(HttpMethod.Get, $"push-contacts/{domain}/visitor-guids")
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
        [InlineData(TestApiUsersData.TOKEN_SUPERUSER_NOTDEFINED_EXPIRE_20330518, "example.com")]
        [InlineData(TestApiUsersData.TOKEN_SUPERUSER_FALSE_EXPIRE_20330518, "example.com")]
        [InlineData(TestApiUsersData.TOKEN_ACCOUNT_123_TEST1_AT_TEST_DOT_COM_EXPIRE_20330518, "example.com")]
        public async Task GetAllVisitorGuidByDomain_should_require_a_valid_token_with_isSU_flag(string token, string domain)
        {
            // Arrange
            var client = _factory.CreateClient(new WebApplicationFactoryClientOptions());

            var request = new HttpRequestMessage(HttpMethod.Get, $"push-contacts/{domain}/visitor-guids")
            {
                Headers = { { "Authorization", $"Bearer {token}" } }
            };

            // Act
            var response = await client.SendAsync(request);
            _output.WriteLine(response.GetHeadersAsString());

            // Assert
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Theory]
        [InlineData("example.com")]
        public async Task GetAllVisitorGuidByDomain_should_return_unauthorized_when_authorization_header_is_empty(string domain)
        {
            // Arrange
            var client = _factory.CreateClient(new WebApplicationFactoryClientOptions());

            var request = new HttpRequestMessage(HttpMethod.Get, $"push-contacts/{domain}/visitor-guids");

            // Act
            var response = await client.SendAsync(request);
            _output.WriteLine(response.GetHeadersAsString());

            // Assert
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Theory]
        [InlineData(" ")]
        public async Task GetAllVisitorGuidByDomain_should_return_bad_request_when_domain_is_whitespace(string domain)
        {
            // Arrange
            var pushContactServiceMock = new Mock<IPushContactService>();
            var messageRepositoryMock = new Mock<IMessageRepository>();
            var webPushEventServiceMock = new Mock<IWebPushEventService>();

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(pushContactServiceMock.Object);
                    services.AddSingleton(messageRepositoryMock.Object);
                    services.AddSingleton(webPushEventServiceMock.Object);
                });

            }).CreateClient(new WebApplicationFactoryClientOptions());

            var url = $"push-contacts/{domain}/visitor-guids";
            var request = new HttpRequestMessage(HttpMethod.Get, url)
            {
                Headers = { { "Authorization", $"Bearer {TestApiUsersData.TOKEN_SUPERUSER_EXPIRE_20330518}" } }
            };

            // Act
            var response = await client.SendAsync(request);
            _output.WriteLine(response.GetHeadersAsString());

            // Assert
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Theory]
        [InlineData("example.com", -1, 1)]
        public async Task GetAllVisitorGuidByDomain_should_return_badrequest_when_page_are_lesser_than_zero(string domain, int _page, int _per_page)
        {
            // Arrange
            var pushContactServiceMock = new Mock<IPushContactService>();
            var messageRepositoryMock = new Mock<IMessageRepository>();
            var webPushEventServiceMock = new Mock<IWebPushEventService>();

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(pushContactServiceMock.Object);
                    services.AddSingleton(messageRepositoryMock.Object);
                    services.AddSingleton(webPushEventServiceMock.Object);
                });

            }).CreateClient(new WebApplicationFactoryClientOptions());

            var request = new HttpRequestMessage(HttpMethod.Get, $"push-contacts/{domain}/visitor-guids?page={_page}&per_page={_per_page}")
            {
                Headers = { { "Authorization", $"Bearer {TestApiUsersData.TOKEN_SUPERUSER_EXPIRE_20330518}" } },
            };

            // Act
            var response = await client.SendAsync(request);
            _output.WriteLine(response.GetHeadersAsString());

            // Assert
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Theory]
        [InlineData("example.com", 0, 0)]
        [InlineData("example.com", 0, -1)]
        public async Task GetAllVisitorGuidByDomain_should_return_badrequest_when_per_page_are_zero_or_lesser(string domain, int _page, int _per_page)
        {
            // Arrange
            var pushContactServiceMock = new Mock<IPushContactService>();
            var messageRepositoryMock = new Mock<IMessageRepository>();
            var webPushEventServiceMock = new Mock<IWebPushEventService>();

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(pushContactServiceMock.Object);
                    services.AddSingleton(messageRepositoryMock.Object);
                    services.AddSingleton(webPushEventServiceMock.Object);
                });

            }).CreateClient(new WebApplicationFactoryClientOptions());

            var request = new HttpRequestMessage(HttpMethod.Get, $"push-contacts/{domain}/visitor-guids?page={_page}&per_page={_per_page}")
            {
                Headers = { { "Authorization", $"Bearer {TestApiUsersData.TOKEN_SUPERUSER_EXPIRE_20330518}" } },
            };

            // Act
            var response = await client.SendAsync(request);
            _output.WriteLine(response.GetHeadersAsString());

            // Assert
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task GetAllVisitorGuidByDomain_should_return_badrequest_when_per_page_is_greater_than_1000()
        {
            // Arrange
            Fixture fixture = new Fixture();
            var domain = fixture.Create<string>();
            var page = 0;
            var per_page_greater_than_1000 = 1001;

            var pushContactServiceMock = new Mock<IPushContactService>();
            var messageRepositoryMock = new Mock<IMessageRepository>();
            var webPushEventServiceMock = new Mock<IWebPushEventService>();

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(pushContactServiceMock.Object);
                    services.AddSingleton(messageRepositoryMock.Object);
                    services.AddSingleton(webPushEventServiceMock.Object);
                });

            }).CreateClient(new WebApplicationFactoryClientOptions());

            var request = new HttpRequestMessage(HttpMethod.Get, $"push-contacts/{domain}/visitor-guids?page={page}&per_page={per_page_greater_than_1000}")
            {
                Headers = { { "Authorization", $"Bearer {TestApiUsersData.TOKEN_SUPERUSER_EXPIRE_20330518}" } },
            };

            // Act
            var response = await client.SendAsync(request);
            _output.WriteLine(response.GetHeadersAsString());

            // Assert
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task GetAllVisitorGuidByDomain_should_return_internal_server_error_when_service_throw_an_exception()
        {
            // Arrange
            Fixture fixture = new Fixture();
            var domain = fixture.Create<string>();
            var page = 0;
            var per_page = 10;

            var pushContactServiceMock = new Mock<IPushContactService>();
            var messageRepositoryMock = new Mock<IMessageRepository>();
            var webPushEventServiceMock = new Mock<IWebPushEventService>();

            pushContactServiceMock
                .Setup(x => x.GetAllVisitorGuidByDomain(domain, page, per_page))
                .ThrowsAsync(new Exception("mocked exception"));

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(pushContactServiceMock.Object);
                    services.AddSingleton(messageRepositoryMock.Object);
                    services.AddSingleton(webPushEventServiceMock.Object);
                });

            }).CreateClient(new WebApplicationFactoryClientOptions());

            var request = new HttpRequestMessage(HttpMethod.Get, $"push-contacts/{domain}/visitor-guids?page={page}&per_page={per_page}")
            {
                Headers = { { "Authorization", $"Bearer {TestApiUsersData.TOKEN_SUPERUSER_EXPIRE_20330518}" } },
            };

            // Act
            var response = await client.SendAsync(request);
            _output.WriteLine(response.GetHeadersAsString());

            // Assert
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        }

        [Fact]
        public async Task GetAllVisitorGuidByDomain_should_return_OK()
        {
            // Arrange
            Fixture fixture = new Fixture();
            var domain = fixture.Create<string>();
            var page = 0;
            var per_page = 10;

            var visitorGuids = new List<string>()
            {
                { "visitor1" },
                { "visitor2" },
            };
            var newPage = page + visitorGuids.Count;

            var apiPageResponse = new ApiPage<string>(visitorGuids, newPage, per_page);

            var pushContactServiceMock = new Mock<IPushContactService>();
            var messageRepositoryMock = new Mock<IMessageRepository>();
            var webPushEventServiceMock = new Mock<IWebPushEventService>();

            pushContactServiceMock
                .Setup(x => x.GetAllVisitorGuidByDomain(domain, page, per_page))
                .ReturnsAsync(apiPageResponse);

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(pushContactServiceMock.Object);
                    services.AddSingleton(messageRepositoryMock.Object);
                    services.AddSingleton(webPushEventServiceMock.Object);
                });

            }).CreateClient(new WebApplicationFactoryClientOptions());

            var request = new HttpRequestMessage(HttpMethod.Get, $"push-contacts/{domain}/visitor-guids?page={page}&per_page={per_page}")
            {
                Headers = { { "Authorization", $"Bearer {TestApiUsersData.TOKEN_SUPERUSER_EXPIRE_20330518}" } },
            };

            // Act
            var response = await client.SendAsync(request);
            _output.WriteLine(response.GetHeadersAsString());

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Theory]
        [InlineData("exampleDomain", "exampleVisitorGuid")]
        public async Task GetEnabledByVisitorGuid_should_return_ok_when_authorization_header_is_empty(string domain, string visitorGuid)
        {
            // Arrange
            var pushContactServiceMock = new Mock<IPushContactService>();
            var messageRepositoryMock = new Mock<IMessageRepository>();
            var webPushEventServiceMock = new Mock<IWebPushEventService>();

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(pushContactServiceMock.Object);
                    services.AddSingleton(messageRepositoryMock.Object);
                    services.AddSingleton(webPushEventServiceMock.Object);
                });

            }).CreateClient(new WebApplicationFactoryClientOptions());

            var request = new HttpRequestMessage(HttpMethod.Get, $"push-contacts/{domain}/{visitorGuid}");

            // Act
            var response = await client.SendAsync(request);
            _output.WriteLine(response.GetHeadersAsString());

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task UpdateSubscription_should_return_bad_request_when_service_throw_argument_exception()
        {
            // Arrange
            var fixture = new Fixture();
            var deviceToken = fixture.Create<string>();
            var subscription = new SubscriptionDTO
            {
                EndPoint = fixture.Create<string>(),
                Keys = new SubscriptionKeys()
                {
                    Auth = fixture.Create<string>(),
                    P256DH = fixture.Create<string>(),
                }
            };

            var pushContactServiceMock = new Mock<IPushContactService>();
            var messageRepositoryMock = new Mock<IMessageRepository>();
            var webPushEventServiceMock = new Mock<IWebPushEventService>();

            pushContactServiceMock
                .Setup(x => x.UpdateSubscriptionAsync(deviceToken, It.IsAny<SubscriptionDTO>()))
                .ThrowsAsync(new ArgumentException());

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(pushContactServiceMock.Object);
                    services.AddSingleton(messageRepositoryMock.Object);
                    services.AddSingleton(webPushEventServiceMock.Object);
                });
            }).CreateClient(new WebApplicationFactoryClientOptions());

            var request = new HttpRequestMessage(HttpMethod.Put, $"push-contacts/{deviceToken}/subscription")
            {
                Content = JsonContent.Create(subscription),
            };

            // Act
            var response = await client.SendAsync(request);
            _output.WriteLine(response.GetHeadersAsString());

            // Assert
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task UpdateSubscription_should_return_internal_server_error_when_service_throw_an_exception()
        {
            // Arrange
            var fixture = new Fixture();
            var deviceToken = fixture.Create<string>();
            var subscription = new SubscriptionDTO
            {
                EndPoint = fixture.Create<string>(),
                Keys = new SubscriptionKeys()
                {
                    Auth = fixture.Create<string>(),
                    P256DH = fixture.Create<string>(),
                }
            };

            var pushContactServiceMock = new Mock<IPushContactService>();
            var messageRepositoryMock = new Mock<IMessageRepository>();
            var webPushEventServiceMock = new Mock<IWebPushEventService>();

            pushContactServiceMock
                .Setup(x => x.UpdateSubscriptionAsync(deviceToken, It.IsAny<SubscriptionDTO>()))
                .ThrowsAsync(new Exception());

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(pushContactServiceMock.Object);
                    services.AddSingleton(messageRepositoryMock.Object);
                    services.AddSingleton(webPushEventServiceMock.Object);
                });
            }).CreateClient(new WebApplicationFactoryClientOptions());

            var request = new HttpRequestMessage(HttpMethod.Put, $"push-contacts/{deviceToken}/subscription")
            {
                Content = JsonContent.Create(subscription),
            };

            // Act
            var response = await client.SendAsync(request);
            _output.WriteLine(response.GetHeadersAsString());

            // Assert
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        }

        [Fact]
        public async Task UpdateSubscription_should_return_not_found_when_service_return_false()
        {
            // Arrange
            var fixture = new Fixture();
            var deviceToken = fixture.Create<string>();
            var subscription = new SubscriptionDTO
            {
                EndPoint = fixture.Create<string>(),
                Keys = new SubscriptionKeys()
                {
                    Auth = fixture.Create<string>(),
                    P256DH = fixture.Create<string>(),
                }
            };

            var pushContactServiceMock = new Mock<IPushContactService>();
            var messageRepositoryMock = new Mock<IMessageRepository>();
            var webPushEventServiceMock = new Mock<IWebPushEventService>();

            pushContactServiceMock
                .Setup(x => x.UpdateSubscriptionAsync(deviceToken, It.IsAny<SubscriptionDTO>()))
                .ReturnsAsync(false);

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(pushContactServiceMock.Object);
                    services.AddSingleton(messageRepositoryMock.Object);
                    services.AddSingleton(webPushEventServiceMock.Object);
                });
            }).CreateClient(new WebApplicationFactoryClientOptions());

            var request = new HttpRequestMessage(HttpMethod.Put, $"push-contacts/{deviceToken}/subscription")
            {
                Content = JsonContent.Create(subscription),
            };

            // Act
            var response = await client.SendAsync(request);
            _output.WriteLine(response.GetHeadersAsString());

            // Assert
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Fact]
        public async Task UpdateSubscription_should_return_OK_when_service_return_true()
        {
            // Arrange
            var fixture = new Fixture();
            var deviceToken = fixture.Create<string>();
            var subscription = new SubscriptionDTO
            {
                EndPoint = fixture.Create<string>(),
                Keys = new SubscriptionKeys()
                {
                    Auth = fixture.Create<string>(),
                    P256DH = fixture.Create<string>(),
                }
            };

            var pushContactServiceMock = new Mock<IPushContactService>();
            var messageRepositoryMock = new Mock<IMessageRepository>();
            var webPushEventServiceMock = new Mock<IWebPushEventService>();

            pushContactServiceMock
                .Setup(x => x.UpdateSubscriptionAsync(deviceToken, It.IsAny<SubscriptionDTO>()))
                .ReturnsAsync(true);

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(pushContactServiceMock.Object);
                    services.AddSingleton(messageRepositoryMock.Object);
                    services.AddSingleton(webPushEventServiceMock.Object);
                });
            }).CreateClient(new WebApplicationFactoryClientOptions());

            var request = new HttpRequestMessage(HttpMethod.Put, $"push-contacts/{deviceToken}/subscription")
            {
                Content = JsonContent.Create(subscription),
            };

            // Act
            var response = await client.SendAsync(request);
            _output.WriteLine(response.GetHeadersAsString());

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task GetMessageDetails_should_return_ok_summarizing_webpushevents_and_message_stats_when_message_has_stats()
        {
            // Arrange
            var fixture = new Fixture();

            var domain = fixture.Create<string>();
            var messageId = fixture.Create<Guid>();

            var pushContactServiceMock = new Mock<IPushContactService>();
            var messageSenderMock = new Mock<IMessageSender>();

            var webPushEventsSummarization = new WebPushEventSummarizationDTO()
            {
                MessageId = messageId,
                SentQuantity = 10,
                Delivered = 1,
                NotDelivered = 9,
            };

            var webPushEventServiceMock = new Mock<IWebPushEventService>();
            webPushEventServiceMock
                .Setup(wpes => wpes.GetWebPushEventSummarizationAsync(messageId))
                .ReturnsAsync(webPushEventsSummarization);

            var messageDetailsWithStats = new MessageDetails()
            {
                MessageId = messageId,
                Domain = domain,
                Sent = 10,
                Delivered = 9,
                NotDelivered = 1,
            };

            var messageRepositoryMock = new Mock<IMessageRepository>();
            messageRepositoryMock
                .Setup(mr => mr.GetMessageDetailsAsync(domain, messageId))
                .ReturnsAsync(messageDetailsWithStats);

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(pushContactServiceMock.Object);
                    services.AddSingleton(messageSenderMock.Object);
                    services.AddSingleton(messageRepositoryMock.Object);
                    services.AddSingleton(webPushEventServiceMock.Object);
                });
            }).CreateClient(new WebApplicationFactoryClientOptions());

            var from = DateTime.UtcNow.AddDays(-1);
            var to = DateTime.UtcNow;

            var request = new HttpRequestMessage(HttpMethod.Get, $"push-contacts/{domain}/messages/{messageId}/details?from={from}&to={to}")
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
            Assert.Equal(messageDetailsWithStats.Sent + webPushEventsSummarization.SentQuantity, result.Sent);
            Assert.Equal(messageDetailsWithStats.Delivered + webPushEventsSummarization.Delivered, result.Delivered);
            Assert.Equal(messageDetailsWithStats.NotDelivered + webPushEventsSummarization.NotDelivered, result.NotDelivered);
        }

        [Fact(Skip = "It doesn't apply anymore. Now the message should always have the summarized stats")]
        public async Task GetMessageDetails_should_return_ok_summarizing_webpushevents_and_historyevents_when_message_has_not_stats()
        {
            // Arrange
            var fixture = new Fixture();

            var domain = fixture.Create<string>();
            var messageId = fixture.Create<Guid>();

            var from = DateTime.UtcNow.AddDays(-1);
            var to = DateTime.UtcNow;

            var messageSenderMock = new Mock<IMessageSender>();

            var webPushEventsSummarization = new WebPushEventSummarizationDTO()
            {
                MessageId = messageId,
                SentQuantity = 10,
                Delivered = 1,
                NotDelivered = 9,
            };

            var webPushEventServiceMock = new Mock<IWebPushEventService>();
            webPushEventServiceMock
                .Setup(wpes => wpes.GetWebPushEventSummarizationAsync(messageId))
                .ReturnsAsync(webPushEventsSummarization);

            var messageDetailsWithStats = new MessageDetails()
            {
                MessageId = messageId,
                Domain = domain,
                Sent = 0,
                Delivered = 0,
                NotDelivered = 0,
            };

            var messageRepositoryMock = new Mock<IMessageRepository>();
            messageRepositoryMock
                .Setup(mr => mr.GetMessageDetailsAsync(domain, messageId))
                .ReturnsAsync(messageDetailsWithStats);

            var messageDeliveryResults = new MessageDeliveryResult()
            {
                Domain = domain,
                SentQuantity = 10,
                Delivered = 9,
                NotDelivered = 1,
            };

            var pushContactServiceMock = new Mock<IPushContactService>();
            pushContactServiceMock
                .Setup(pcs => pcs.GetDeliveredMessageSummarizationAsync(domain, messageId, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>()))
                .ReturnsAsync(messageDeliveryResults);

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(pushContactServiceMock.Object);
                    services.AddSingleton(messageSenderMock.Object);
                    services.AddSingleton(messageRepositoryMock.Object);
                    services.AddSingleton(webPushEventServiceMock.Object);
                });
            }).CreateClient(new WebApplicationFactoryClientOptions());

            var request = new HttpRequestMessage(HttpMethod.Get, $"push-contacts/{domain}/messages/{messageId}/details?from={from}&to={to}")
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
            Assert.Equal(messageDeliveryResults.SentQuantity + webPushEventsSummarization.SentQuantity, result.Sent);
            Assert.Equal(messageDeliveryResults.Delivered + webPushEventsSummarization.Delivered, result.Delivered);
            Assert.Equal(messageDeliveryResults.NotDelivered + webPushEventsSummarization.NotDelivered, result.NotDelivered);
        }

        [Theory(Skip = "avoid intermittent problems with EncryptionHelper/Decrypt")]
        [InlineData(
            "66291accdc3ab636288af4ab",
            "swxCTS4gQMVIsaM-WpP_LaWIp2xwGpP-r3Md_pt1Jsk",
            "df555721-5135-4b5d-9c6a-7db3565f22ae",
            "uTOloRjtZHhcyWtDOm0p_v3J6r4Y9Q7o6zSfFDzKZJFFxJ_6PWE_lMZ5-mJ1aJ_B",
            WebPushEventType.Clicked,
            HttpStatusCode.Accepted
        )]
        [InlineData(
            "66291accdc3ab636288af4ab",
            "swxCTS4gQMVIsaM-WpP_LaWIp2xwGpP-r3Md_pt1Jsk",
            "df555721-5135-4b5d-9c6a-7db3565f22ae",
            "uTOloRjtZHhcyWtDOm0p_v3J6r4Y9Q7o6zSfFDzKZJFFxJ_6PWE_lMZ5-mJ1aJ_B",
            WebPushEventType.Received,
            HttpStatusCode.Accepted
        )]
        [InlineData(
            "66291accdc3ab636288af4ab",
            "invalidEncryptedContactId",
            "df555721-5135-4b5d-9c6a-7db3565f22ae",
            "uTOloRjtZHhcyWtDOm0p_v3J6r4Y9Q7o6zSfFDzKZJFFxJ_6PWE_lMZ5-mJ1aJ_B",
            WebPushEventType.Clicked,
            HttpStatusCode.BadRequest
        )]
        [InlineData(
            "66291accdc3ab636288af4ab",
            "swxCTS4gQMVIsaM-WpP_LaWIp2xwGpP-r3Md_pt1Jsk",
            "df555721-5135-4b5d-9c6a-7db3565f22ae",
            "invalidEncryptedMessageId",
            WebPushEventType.Clicked,
            HttpStatusCode.BadRequest
        )]
        [InlineData(
            "66291accdc3ab636288af4ab",
            "invalidEncryptedContactId",
            "df555721-5135-4b5d-9c6a-7db3565f22ae",
            "uTOloRjtZHhcyWtDOm0p_v3J6r4Y9Q7o6zSfFDzKZJFFxJ_6PWE_lMZ5-mJ1aJ_B",
            WebPushEventType.Received,
            HttpStatusCode.BadRequest
        )]
        [InlineData(
            "66291accdc3ab636288af4ab",
            "swxCTS4gQMVIsaM-WpP_LaWIp2xwGpP-r3Md_pt1Jsk",
            "df555721-5135-4b5d-9c6a-7db3565f22ae",
            "invalidEncryptedMessageId",
            WebPushEventType.Received,
            HttpStatusCode.BadRequest
        )]
        public async Task RegisterWebPushEvent_should_return_expected_status_code_and_call_to_service_properly(
            string contactId,
            string encryptedContactId,
            Guid messageId,
            string encryptedMessageId,
            WebPushEventType type,
            HttpStatusCode expectedStatusCode
        )
        {
            // Arrange
            var pushContactServiceMock = new Mock<IPushContactService>();
            var messageRepositoryMock = new Mock<IMessageRepository>();
            var webPushEventServiceMock = new Mock<IWebPushEventService>();
            var pushContactRepository = new Mock<IPushContactRepository>();

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(pushContactServiceMock.Object);
                    services.AddSingleton(messageRepositoryMock.Object);
                    services.AddSingleton(webPushEventServiceMock.Object);
                    services.AddSingleton(pushContactRepository.Object);

                    var TestKey = "5Rz2VJbnjbhPfEKn3Ryd0E+u7jzOT2KCBicmM5wUq5Y=";
                    var TestIV = "7yZ8kT8L7UeO8JpH3Ir6jQ==";
                    EncryptionHelper.Initialize(TestKey, TestIV);
                });

            }).CreateClient(new WebApplicationFactoryClientOptions());

            var requestUri = $"/push-contacts/{encryptedContactId}/messages/{encryptedMessageId}/{type.ToString().ToLower()}";
            var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
            {
                Headers = { { "Authorization", $"Bearer {TestApiUsersData.TOKEN_SUPERUSER_EXPIRE_20330518}" } }
            };

            // Act
            var response = await client.SendAsync(request);
            _output.WriteLine(response.GetHeadersAsString());

            // Assert
            Assert.Equal(expectedStatusCode, response.StatusCode);

            if (expectedStatusCode == HttpStatusCode.Accepted)
            {
                webPushEventServiceMock.Verify(
                    x => x.RegisterWebPushEventAsync(
                        contactId,
                        messageId,
                        type,
                        It.IsAny<CancellationToken>()),
                    Times.Once);
            }
        }

        [Theory]
        [InlineData(TestApiUsersData.TOKEN_EMPTY)]
        [InlineData(TestApiUsersData.TOKEN_BROKEN)]
        public async Task GetDistinctVisitorGuidByDomain_should_return_unauthorized_when_token_is_not_valid(string token)
        {
            // Arrange
            var client = _factory.CreateClient(new WebApplicationFactoryClientOptions());

            var request = new HttpRequestMessage(HttpMethod.Get, $"push-contacts/visitor-guids")
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
        public async Task GetDistinctVisitorGuidByDomain_should_return_unauthorized_when_token_is_a_expired_superuser_token(string token)
        {
            // Arrange
            var client = _factory.CreateClient(new WebApplicationFactoryClientOptions());

            var request = new HttpRequestMessage(HttpMethod.Get, $"push-contacts/visitor-guids")
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
        public async Task GetDistinctVisitorGuidByDomain_should_require_a_valid_token_with_isSU_flag(string token)
        {
            // Arrange
            var client = _factory.CreateClient(new WebApplicationFactoryClientOptions());

            var request = new HttpRequestMessage(HttpMethod.Get, $"push-contacts/visitor-guids")
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
        public async Task GetDistinctVisitorGuidByDomains_should_return_unauthorized_when_authorization_header_is_empty()
        {
            // Arrange
            var client = _factory.CreateClient(new WebApplicationFactoryClientOptions());

            var request = new HttpRequestMessage(HttpMethod.Get, $"push-contacts/visitor-guids");

            // Act
            var response = await client.SendAsync(request);
            _output.WriteLine(response.GetHeadersAsString());

            // Assert
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Theory]
        [InlineData(" ")]
        [InlineData("")]
        [InlineData(null)]
        public async Task GetDistinctVisitorGuidByDomains_should_return_bad_request_when_domain_is_whitespace_or_empty_or_null(string domain)
        {
            // Arrange
            var pushContactServiceMock = new Mock<IPushContactService>();
            var messageRepositoryMock = new Mock<IMessageRepository>();
            var webPushEventServiceMock = new Mock<IWebPushEventService>();

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(pushContactServiceMock.Object);
                    services.AddSingleton(messageRepositoryMock.Object);
                    services.AddSingleton(webPushEventServiceMock.Object);
                });

            }).CreateClient(new WebApplicationFactoryClientOptions());

            var url = $"push-contacts/visitor-guids?domain={domain}";
            var request = new HttpRequestMessage(HttpMethod.Get, url)
            {
                Headers = { { "Authorization", $"Bearer {TestApiUsersData.TOKEN_SUPERUSER_EXPIRE_20330518}" } }
            };

            // Act
            var response = await client.SendAsync(request);
            _output.WriteLine(response.GetHeadersAsString());

            // Assert
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(0)]
        [InlineData(1001)]
        public async Task GetDistinctVisitorGuidByDomains_should_return_badrequest_when_per_page_is_zero_or_lesser_than_zero_or_greater_than_1000(int per_page)
        {
            // Arrange
            Fixture fixture = new Fixture();
            var domain = fixture.Create<string>();

            var pushContactServiceMock = new Mock<IPushContactService>();
            var messageRepositoryMock = new Mock<IMessageRepository>();
            var webPushEventServiceMock = new Mock<IWebPushEventService>();

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(pushContactServiceMock.Object);
                    services.AddSingleton(messageRepositoryMock.Object);
                    services.AddSingleton(webPushEventServiceMock.Object);
                });

            }).CreateClient(new WebApplicationFactoryClientOptions());

            var request = new HttpRequestMessage(HttpMethod.Get, $"push-contacts/visitor-guids?domain={domain}&per_page={per_page}")
            {
                Headers = { { "Authorization", $"Bearer {TestApiUsersData.TOKEN_SUPERUSER_EXPIRE_20330518}" } },
            };

            // Act
            var response = await client.SendAsync(request);
            _output.WriteLine(response.GetHeadersAsString());

            // Assert
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task GetDistinctVisitorGuidByDomains_should_return_internal_server_error_when_service_throw_an_exception()
        {
            // Arrange
            Fixture fixture = new Fixture();
            var domain = fixture.Create<string>();
            var nextCursor = fixture.Create<string>();
            var per_page = 10;

            var pushContactServiceMock = new Mock<IPushContactService>();
            var messageRepositoryMock = new Mock<IMessageRepository>();
            var webPushEventServiceMock = new Mock<IWebPushEventService>();

            pushContactServiceMock
                .Setup(x => x.GetDistinctVisitorGuidByDomain(domain, nextCursor, per_page))
                .ThrowsAsync(new Exception("mocked exception"));

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(pushContactServiceMock.Object);
                    services.AddSingleton(messageRepositoryMock.Object);
                    services.AddSingleton(webPushEventServiceMock.Object);
                });

            }).CreateClient(new WebApplicationFactoryClientOptions());

            var request = new HttpRequestMessage(HttpMethod.Get, $"push-contacts/visitor-guids?domain={domain}&nextCursor={nextCursor}&per_page={per_page}")
            {
                Headers = { { "Authorization", $"Bearer {TestApiUsersData.TOKEN_SUPERUSER_EXPIRE_20330518}" } },
            };

            // Act
            var response = await client.SendAsync(request);
            _output.WriteLine(response.GetHeadersAsString());

            // Assert
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        }

        [Fact]
        public async Task GetDistinctVisitorGuidByDomains_should_return_OK()
        {
            // Arrange
            Fixture fixture = new Fixture();
            var domain = fixture.Create<string>();
            var nextCursorToInit = fixture.Create<string>();
            var per_page = 10;

            var visitorGuids = new List<string>()
            {
                { "visitor1" },
                { "visitor2" },
            };
            var nextCursorToContinue = fixture.Create<string>();

            var cursorPageResponse = new CursorPage<string>(visitorGuids, nextCursorToContinue, per_page);

            var pushContactServiceMock = new Mock<IPushContactService>();
            var messageRepositoryMock = new Mock<IMessageRepository>();
            var webPushEventServiceMock = new Mock<IWebPushEventService>();

            pushContactServiceMock
                .Setup(x => x.GetDistinctVisitorGuidByDomain(domain, nextCursorToInit, per_page))
                .ReturnsAsync(cursorPageResponse);

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(pushContactServiceMock.Object);
                    services.AddSingleton(messageRepositoryMock.Object);
                    services.AddSingleton(webPushEventServiceMock.Object);
                });

            }).CreateClient(new WebApplicationFactoryClientOptions());

            var request = new HttpRequestMessage(HttpMethod.Get, $"push-contacts/visitor-guids?domain={domain}&nextCursor={nextCursorToInit}&per_page={per_page}")
            {
                Headers = { { "Authorization", $"Bearer {TestApiUsersData.TOKEN_SUPERUSER_EXPIRE_20330518}" } },
            };

            // Act
            var response = await client.SendAsync(request);
            var contentString = await response.Content.ReadAsStringAsync();
            _output.WriteLine(contentString);

            // Assert
            var result = JsonSerializer.Deserialize<CursorPage<string>>(contentString, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.NotNull(result);

            Assert.Equal(cursorPageResponse.Items, result.Items);
            Assert.Equal(cursorPageResponse.NextCursor, result.NextCursor);
            Assert.Equal(cursorPageResponse.PerPage, result.PerPage);
        }
    }
}
