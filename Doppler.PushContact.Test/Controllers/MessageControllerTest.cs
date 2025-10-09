using AutoFixture;
using Doppler.PushContact.Controllers;
using Doppler.PushContact.DTOs;
using Doppler.PushContact.Models;
using Doppler.PushContact.Models.DTOs;
using Doppler.PushContact.Models.Models;
using Doppler.PushContact.Repositories.Interfaces;
using Doppler.PushContact.Services;
using Doppler.PushContact.Services.Messages;
using Doppler.PushContact.Test.Controllers.Utils;
using Loggly;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Doppler.PushContact.Test.Controllers
{
    public class MessageControllerTest : IClassFixture<WebApplicationFactory<Startup>>
    {
        private readonly WebApplicationFactory<Startup> _factory;
        private readonly ITestOutputHelper _output;

        public MessageControllerTest(WebApplicationFactory<Startup> factory, ITestOutputHelper output)
        {
            _factory = factory;
            _output = output;
        }

        [Theory]
        [InlineData(TestApiUsersData.TOKEN_EMPTY)]
        [InlineData(TestApiUsersData.TOKEN_BROKEN)]
        public async Task MessageByVisitorGuid_should_return_unauthorized_when_token_is_not_valid(string token)
        {
            // Arrange
            var client = _factory.CreateClient(new WebApplicationFactoryClientOptions());

            var fixture = new Fixture();
            var visitorGuid = fixture.Create<string>();
            var messageId = fixture.Create<Guid>();

            var request = new HttpRequestMessage(HttpMethod.Post, $"message/{messageId}")
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
        public async Task MessageByVisitorGuid_should_return_unauthorized_when_token_is_a_expired_superuser_token(string token)
        {
            // Arrange
            var client = _factory.CreateClient(new WebApplicationFactoryClientOptions());

            var fixture = new Fixture();
            var visitorGuid = fixture.Create<string>();
            var messageId = fixture.Create<Guid>();

            var request = new HttpRequestMessage(HttpMethod.Post, $"message/{messageId}")
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
        public async Task MessageByVisitorGuid_should_require_a_valid_token_with_isSU_flag(string token)
        {
            // Arrange
            var client = _factory.CreateClient(new WebApplicationFactoryClientOptions());

            var fixture = new Fixture();
            var visitorGuid = fixture.Create<string>();
            var messageId = fixture.Create<Guid>();

            var request = new HttpRequestMessage(HttpMethod.Post, $"message/{messageId}")
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
        public async Task MessageByVisitorGuid_should_return_unauthorized_when_authorization_header_is_empty()
        {
            // Arrange
            var client = _factory.CreateClient(new WebApplicationFactoryClientOptions());

            var fixture = new Fixture();
            var visitorGuid = fixture.Create<string>();
            var messageId = fixture.Create<Guid>();

            var request = new HttpRequestMessage(HttpMethod.Post, $"message/{messageId}")
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
        public async Task MessageByVisitorGuid_should_return_internal_server_error_when_service_throw_an_exception()
        {
            // Arrange
            var fixture = new Fixture();

            var visitorGuid = fixture.Create<string>();
            var domain = fixture.Create<string>();
            var messageId = fixture.Create<Guid>();

            var messageRepositoryMock = new Mock<IMessageRepository>();

            messageRepositoryMock
                .Setup(x => x.GetMessageDetailsAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>()))
                .ThrowsAsync(new Exception());

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(messageRepositoryMock.Object);
                });

            }).CreateClient(new WebApplicationFactoryClientOptions());

            var request = new HttpRequestMessage(HttpMethod.Post, $"message/{messageId}")
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
        [InlineData(" ", "ccf7ad9b-bd9a-465a-b240-602c93140bf3")]
        public async Task MessageByVisitorGuid_should_return_bad_request_when_messageId_is_whitespace(string visitorGuid, string messageId)
        {
            // Arrange
            var fixture = new Fixture();

            var pushContactServiceMock = new Mock<IPushContactService>();
            var messageRepositoryMock = new Mock<IMessageRepository>();
            var messageSenderMock = new Mock<IMessageSender>();
            var pushContactRepository = new Mock<IPushContactRepository>();

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(pushContactServiceMock.Object);
                    services.AddSingleton(messageSenderMock.Object);
                    services.AddSingleton(messageRepositoryMock.Object);
                    services.AddSingleton(pushContactRepository.Object);
                });

            }).CreateClient(new WebApplicationFactoryClientOptions());

            var request = new HttpRequestMessage(HttpMethod.Post, $"message/{messageId}")
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
        [InlineData("", "exampleMessageId")]
        [InlineData(" ", "exampleMessageId")]
        [InlineData(null, "exampleMessageId")]
        public async Task MessageByVisitorGuid_should_return_bad_request_when_visitor_guid_is_null_empty_or_whitespace(string visitorGuid, string messageId)
        {
            // Arrange
            var fixture = new Fixture();

            var pushContactServiceMock = new Mock<IPushContactService>();
            var messageRepositoryMock = new Mock<IMessageRepository>();
            var messageSenderMock = new Mock<IMessageSender>();
            var pushContactRepository = new Mock<IPushContactRepository>();

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(pushContactServiceMock.Object);
                    services.AddSingleton(messageSenderMock.Object);
                    services.AddSingleton(messageRepositoryMock.Object);
                    services.AddSingleton(pushContactRepository.Object);
                });

            }).CreateClient(new WebApplicationFactoryClientOptions());

            var request = new HttpRequestMessage(HttpMethod.Post, $"message/{messageId}")
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
        public async Task CreateMessage_should_return_unauthorized_when_token_is_not_valid(string token)
        {
            // Arrange
            var client = _factory.CreateClient(new WebApplicationFactoryClientOptions());

            var fixture = new Fixture();
            var name = fixture.Create<string>();

            var request = new HttpRequestMessage(HttpMethod.Post, $"message")
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
        public async Task CreateMessage_should_return_unauthorized_when_token_is_a_expired_superuser_token(string token)
        {
            // Arrange
            var client = _factory.CreateClient(new WebApplicationFactoryClientOptions());

            var request = new HttpRequestMessage(HttpMethod.Post, $"message")
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
        public async Task CreateMessage_should_require_a_valid_token_with_isSU_flag(string token)
        {
            // Arrange
            var client = _factory.CreateClient(new WebApplicationFactoryClientOptions());

            var request = new HttpRequestMessage(HttpMethod.Post, $"message")
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
        public async Task CreateMessage_should_return_unauthorized_when_authorization_header_is_empty()
        {
            // Arrange
            var client = _factory.CreateClient(new WebApplicationFactoryClientOptions());

            var request = new HttpRequestMessage(HttpMethod.Post, $"message");

            // Act
            var response = await client.SendAsync(request);
            _output.WriteLine(response.GetHeadersAsString());

            // Assert
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task CreateMessage_should_create_a_message_OK_and_return_proper_messageId()
        {
            // Arrange
            var fixture = new Fixture();

            var message = new MessageBody
            {
                Message = new Message()
                {
                    Title = fixture.Create<string>(),
                    Body = fixture.Create<string>(),
                    OnClickLink = fixture.Create<string>(),
                    ImageUrl = fixture.Create<string>()
                },
                Domain = fixture.Create<string>()
            };

            var pushContactService = new Mock<IPushContactService>();
            var messageServiceMock = new Mock<IMessageService>();
            var messageRepositoryMock = new Mock<IMessageRepository>();
            var messageSenderMock = new Mock<IMessageSender>();
            var pushContactRepository = new Mock<IPushContactRepository>();

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(pushContactService.Object);
                    services.AddSingleton(messageServiceMock.Object);
                    services.AddSingleton(messageRepositoryMock.Object);
                    services.AddSingleton(messageSenderMock.Object);
                    services.AddSingleton(pushContactRepository.Object);
                });
            }).CreateClient(new WebApplicationFactoryClientOptions());

            var request = new HttpRequestMessage(HttpMethod.Post, $"message")
            {
                Headers = { { "Authorization", $"Bearer {TestApiUsersData.TOKEN_SUPERUSER_EXPIRE_20330518}" } },
                Content = JsonContent.Create(message)
            };

            // Act
            var response = await client.SendAsync(request);
            _output.WriteLine(response.GetHeadersAsString());

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            messageServiceMock.Verify(mock => mock.AddMessageAsync(
                It.Is<MessageDTO>(dto =>
                    dto.Domain == message.Domain &&
                    dto.Title == message.Message.Title &&
                    dto.Body == message.Message.Body &&
                    dto.OnClickLink == message.Message.OnClickLink &&
                    dto.ImageUrl == message.Message.ImageUrl &&
                    dto.Actions != null && dto.Actions.Count == 0
                )
            ), Times.Once());

            var messageResult = await response.Content.ReadFromJsonAsync<MessageResult>();
            Assert.IsType<Guid>(messageResult.MessageId);
        }

        [Fact]
        public async Task CreateMessage_should_create_a_message_with_actions_OK_and_return_proper_messageId()
        {
            // Arrange
            var fixture = new Fixture();

            var action1 = new MessageAction()
            {
                Action = fixture.Create<string>(),
                Title = fixture.Create<string>(),
                Icon = fixture.Create<string>(),
                Link = fixture.Create<string>(),
            };

            var actions = new List<MessageAction>() { action1 };

            var message = new MessageBody
            {
                Message = new Message()
                {
                    Title = fixture.Create<string>(),
                    Body = fixture.Create<string>(),
                    OnClickLink = fixture.Create<string>(),
                    ImageUrl = fixture.Create<string>(),
                    Actions = actions,
                },
                Domain = fixture.Create<string>()
            };

            var pushContactService = new Mock<IPushContactService>();
            var messageServiceMock = new Mock<IMessageService>();
            var messageRepositoryMock = new Mock<IMessageRepository>();
            var messageSenderMock = new Mock<IMessageSender>();
            var pushContactRepository = new Mock<IPushContactRepository>();

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(pushContactService.Object);
                    services.AddSingleton(messageServiceMock.Object);
                    services.AddSingleton(messageRepositoryMock.Object);
                    services.AddSingleton(messageSenderMock.Object);
                    services.AddSingleton(pushContactRepository.Object);
                });
            }).CreateClient(new WebApplicationFactoryClientOptions());

            var request = new HttpRequestMessage(HttpMethod.Post, $"message")
            {
                Headers = { { "Authorization", $"Bearer {TestApiUsersData.TOKEN_SUPERUSER_EXPIRE_20330518}" } },
                Content = JsonContent.Create(message)
            };

            // Act
            var response = await client.SendAsync(request);
            _output.WriteLine(response.GetHeadersAsString());

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            messageServiceMock.Verify(mock => mock.AddMessageAsync(
                It.Is<MessageDTO>(dto =>
                    dto.Domain == message.Domain &&
                    dto.Title == message.Message.Title &&
                    dto.Body == message.Message.Body &&
                    dto.OnClickLink == message.Message.OnClickLink &&
                    dto.ImageUrl == message.Message.ImageUrl &&
                    dto.Actions != null && dto.Actions.Count == 1
                )
            ), Times.Once());

            var messageResult = await response.Content.ReadFromJsonAsync<MessageResult>();
            Assert.IsType<Guid>(messageResult.MessageId);
        }

        [Fact]
        public async Task CreateMessage_should_return_BadRequest_when_service_throw_argumentexception()
        {
            // Arrange
            var fixture = new Fixture();

            var message = new MessageBody
            {
                Message = new Message()
                {
                    Title = fixture.Create<string>(),
                    Body = fixture.Create<string>()
                },
                Domain = fixture.Create<string>()
            };

            var pushContactService = new Mock<IPushContactService>();
            var messageRepositoryMock = new Mock<IMessageRepository>();
            var messageServiceMock = new Mock<IMessageService>();
            var messageSenderMock = new Mock<IMessageSender>();
            var pushContactRepository = new Mock<IPushContactRepository>();


            messageServiceMock
                .Setup(x => x.AddMessageAsync(It.IsAny<MessageDTO>()))
                .Throws(new ArgumentException());

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(pushContactService.Object);
                    services.AddSingleton(messageRepositoryMock.Object);
                    services.AddSingleton(messageServiceMock.Object);
                    services.AddSingleton(messageSenderMock.Object);
                    services.AddSingleton(pushContactRepository.Object);
                });
            }).CreateClient(new WebApplicationFactoryClientOptions());

            var request = new HttpRequestMessage(HttpMethod.Post, $"message")
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

        [Theory]
        [InlineData("", "aTitle", "aBody")]
        [InlineData(" ", "aTitle", "aBody")]
        [InlineData(null, "aTitle", "aBody")]
        [InlineData("domain.com", "", "aBody")]
        [InlineData("domain.com", " ", "aBody")]
        [InlineData("domain.com", null, "aBody")]
        [InlineData("domain.com", "aTitle", "")]
        [InlineData("domain.com", "aTitle", " ")]
        [InlineData("domain.com", "aTitle", null)]
        public async Task CreateMessage_should_return_BadRequest_error_when_domain_or_title_or_body_are_missing(string domain, string title, string body)
        {
            // Arrange
            var message = new MessageBody
            {
                Message = new Message()
                {
                    Title = title,
                    Body = body
                },
                Domain = domain
            };

            var pushContactService = new Mock<IPushContactService>();
            var messageRepositoryMock = new Mock<IMessageRepository>();
            var messageSenderMock = new Mock<IMessageSender>();
            var pushContactRepository = new Mock<IPushContactRepository>();

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(pushContactService.Object);
                    services.AddSingleton(messageRepositoryMock.Object);
                    services.AddSingleton(messageSenderMock.Object);
                    services.AddSingleton(pushContactRepository.Object);
                });
            }).CreateClient(new WebApplicationFactoryClientOptions());

            var request = new HttpRequestMessage(HttpMethod.Post, $"message")
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
        public async Task CreateMessage_should_return_BadRequest_error_when_message_field_is_missing()
        {
            var fixture = new Fixture();

            // Arrange
            var message = new MessageBody
            {
                Domain = fixture.Create<string>()
            };

            var pushContactService = new Mock<IPushContactService>();
            var messageRepositoryMock = new Mock<IMessageRepository>();
            var messageSenderMock = new Mock<IMessageSender>();
            var pushContactRepository = new Mock<IPushContactRepository>();

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(pushContactService.Object);
                    services.AddSingleton(messageRepositoryMock.Object);
                    services.AddSingleton(messageSenderMock.Object);
                    services.AddSingleton(pushContactRepository.Object);
                });
            }).CreateClient(new WebApplicationFactoryClientOptions());

            var request = new HttpRequestMessage(HttpMethod.Post, $"message")
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

        [Theory]
        [InlineData(TestApiUsersData.TOKEN_EMPTY)]
        [InlineData(TestApiUsersData.TOKEN_BROKEN)]
        public async Task ProcessWebPushByDomain_should_return_unauthorized_when_token_is_not_valid(string token)
        {
            // Arrange
            var client = _factory.CreateClient(new WebApplicationFactoryClientOptions());

            var fixture = new Fixture();
            var domain = fixture.Create<string>();

            var request = new HttpRequestMessage(HttpMethod.Post, $"messages/domains/{domain}")
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
        public async Task ProcessWebPushByDomain_should_return_unauthorized_when_token_is_an_expired_superuser_token(string token)
        {
            // Arrange
            var client = _factory.CreateClient(new WebApplicationFactoryClientOptions());

            var fixture = new Fixture();
            var domain = fixture.Create<string>();

            var request = new HttpRequestMessage(HttpMethod.Post, $"messages/domains/{domain}")
            {
                Headers = { { "Authorization", $"Bearer {token}" } }
            };

            // Act
            var response = await client.SendAsync(request);
            _output.WriteLine(response.GetHeadersAsString());

            // Assert
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task ProcessWebPushByDomain_should_return_unauthorized_when_authorization_header_is_not_defined()
        {
            // Arrange
            var client = _factory.CreateClient(new WebApplicationFactoryClientOptions());

            var fixture = new Fixture();
            var domain = fixture.Create<string>();

            var request = new HttpRequestMessage(HttpMethod.Post, $"messages/domains/{domain}");

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
        public async Task ProcessWebPushByDomain_should_require_a_valid_token_with_isSU_flag(string token)
        {
            // Arrange
            var client = _factory.CreateClient(new WebApplicationFactoryClientOptions());

            var fixture = new Fixture();
            var domain = fixture.Create<string>();

            var request = new HttpRequestMessage(HttpMethod.Post, $"messages/domains/{domain}")
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
        public async Task ProcessWebPushByDomain_should_return_BadRequest_when_MessageService_throws_an_ArgumentException()
        {
            var fixture = new Fixture();

            // Arrange
            var domain = fixture.Create<string>();
            var message = new Message
            {
                Title = fixture.Create<string>(),
                Body = fixture.Create<string>()
            };

            var pushContactService = new Mock<IPushContactService>();
            var messageRepositoryMock = new Mock<IMessageRepository>();
            var messageServiceMock = new Mock<IMessageService>();
            var messageSenderMock = new Mock<IMessageSender>();
            var pushContactRepository = new Mock<IPushContactRepository>();

            messageServiceMock
            .Setup(x => x.AddMessageAsync(It.IsAny<MessageDTO>()))
            .Throws<ArgumentException>();

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(pushContactService.Object);
                    services.AddSingleton(messageRepositoryMock.Object);
                    services.AddSingleton(messageServiceMock.Object);
                    services.AddSingleton(messageSenderMock.Object);
                    services.AddSingleton(pushContactRepository.Object);
                });
            }).CreateClient(new WebApplicationFactoryClientOptions());

            var request = new HttpRequestMessage(HttpMethod.Post, $"messages/domains/{domain}")
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
        public async Task ProcessWebPushByDomain_should_return_InternalServerError_when_MessageService_throws_an_Exception()
        {
            var fixture = new Fixture();

            // Arrange
            var domain = fixture.Create<string>();
            var message = new Message
            {
                Title = fixture.Create<string>(),
                Body = fixture.Create<string>()
            };

            var pushContactService = new Mock<IPushContactService>();
            var messageRepositoryMock = new Mock<IMessageRepository>();
            var messageServiceMock = new Mock<IMessageService>();
            var messageSenderMock = new Mock<IMessageSender>();
            var loggerMock = new Mock<ILogger<MessageController>>();
            var pushContactRepository = new Mock<IPushContactRepository>();

            var expectedException = new Exception("my exception on testing");

            messageServiceMock
            .Setup(x => x.AddMessageAsync(It.IsAny<MessageDTO>()))
            .Throws(expectedException);

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(pushContactService.Object);
                    services.AddSingleton(messageRepositoryMock.Object);
                    services.AddSingleton(messageServiceMock.Object);
                    services.AddSingleton(messageSenderMock.Object);
                    services.AddSingleton(loggerMock.Object);
                    services.AddSingleton(pushContactRepository.Object);
                });
            }).CreateClient(new WebApplicationFactoryClientOptions());

            var request = new HttpRequestMessage(HttpMethod.Post, $"messages/domains/{domain}")
            {
                Headers = { { "Authorization", $"Bearer {TestApiUsersData.TOKEN_SUPERUSER_EXPIRE_20330518}" } },
                Content = JsonContent.Create(message)
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
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains($"An unexpected error occurred adding a message for domain: {domain}")),
                    It.Is<Exception>(ex => ex == expectedException),
                    It.Is<Func<It.IsAnyType, Exception, string>>((v, t) => true)),
                Times.Once);
        }

        [Fact]
        public async Task ProcessWebPushByDomain_should_return_Ok_and_a_valid_messageId()
        {
            var fixture = new Fixture();

            // Arrange
            var domain = fixture.Create<string>();
            var title = fixture.Create<string>();
            var body = fixture.Create<string>();
            var message = new Message
            {
                Title = title,
                Body = body,
            };

            var pushContactService = new Mock<IPushContactService>();
            var messageRepositoryMock = new Mock<IMessageRepository>();
            var messageServiceMock = new Mock<IMessageService>();
            var messageSenderMock = new Mock<IMessageSender>();
            var webPushPublisherServiceMock = new Mock<IWebPushPublisherService>();

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(pushContactService.Object);
                    services.AddSingleton(messageRepositoryMock.Object);
                    services.AddSingleton(messageServiceMock.Object);
                    services.AddSingleton(messageSenderMock.Object);
                    services.AddSingleton(webPushPublisherServiceMock.Object);
                });
            }).CreateClient(new WebApplicationFactoryClientOptions());

            var request = new HttpRequestMessage(HttpMethod.Post, $"messages/domains/{domain}")
            {
                Headers = { { "Authorization", $"Bearer {TestApiUsersData.TOKEN_SUPERUSER_EXPIRE_20330518}" } },
                Content = JsonContent.Create(message)
            };

            // Act
            var response = await client.SendAsync(request);
            _output.WriteLine(response.GetHeadersAsString());

            // Assert
            // verify ProcessWebPushInBatches was called once
            webPushPublisherServiceMock.Verify(x => x.ProcessWebPushByDomainInBatches(domain, It.IsAny<WebPushDTO>(), It.IsAny<string>()), Times.Once);

            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

            var responseBody = await response.Content.ReadAsStringAsync();
            // ignore upper and lower case on deserializing
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
            var responseObject = JsonSerializer.Deserialize<MessageResult>(responseBody, options);
            var messageId = responseObject?.MessageId;

            Assert.NotNull(messageId);
            Assert.True(Guid.TryParse(messageId.ToString(), out _), $"Expected a valid GUID but got '{messageId}'.");
        }

        [Theory]
        [InlineData(TestApiUsersData.TOKEN_EMPTY)]
        [InlineData(TestApiUsersData.TOKEN_BROKEN)]
        public async Task ProcessWebPushForVisitorGuid_should_return_unauthorized_when_token_is_not_valid(string token)
        {
            // Arrange
            var client = _factory.CreateClient(new WebApplicationFactoryClientOptions());

            var fixture = new Fixture();
            var messageId = fixture.Create<Guid>();
            var visitorGuid = fixture.Create<string>();

            var request = new HttpRequestMessage(HttpMethod.Post, $"messages/{messageId}/visitors/{visitorGuid}/send")
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
        public async Task ProcessWebPushForVisitorGuid_should_return_unauthorized_when_token_is_an_expired_superuser_token(string token)
        {
            // Arrange
            var client = _factory.CreateClient(new WebApplicationFactoryClientOptions());

            var fixture = new Fixture();
            var messageId = fixture.Create<Guid>();
            var visitorGuid = fixture.Create<string>();

            var request = new HttpRequestMessage(HttpMethod.Post, $"messages/{messageId}/visitors/{visitorGuid}/send")
            {
                Headers = { { "Authorization", $"Bearer {token}" } }
            };

            // Act
            var response = await client.SendAsync(request);
            _output.WriteLine(response.GetHeadersAsString());

            // Assert
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task ProcessWebPushForVisitorGuid_should_return_unauthorized_when_authorization_header_is_not_defined()
        {
            // Arrange
            var client = _factory.CreateClient(new WebApplicationFactoryClientOptions());

            var fixture = new Fixture();
            var messageId = fixture.Create<Guid>();
            var visitorGuid = fixture.Create<string>();

            var request = new HttpRequestMessage(HttpMethod.Post, $"messages/{messageId}/visitors/{visitorGuid}/send");

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
        public async Task ProcessWebPushForVisitorGuid_should_require_a_token_with_isSU_flag(string token)
        {
            // Arrange
            var client = _factory.CreateClient(new WebApplicationFactoryClientOptions());

            var fixture = new Fixture();
            var messageId = fixture.Create<Guid>();
            var visitorGuid = fixture.Create<string>();

            var request = new HttpRequestMessage(HttpMethod.Post, $"messages/{messageId}/visitors/{visitorGuid}/send")
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
        public async Task ProcessWebPushForVisitorGuid_should_return_NotFound_when_message_doesnot_exist()
        {
            var fixture = new Fixture();

            // Arrange
            var messageId = fixture.Create<Guid>();
            var visitorGuid = fixture.Create<string>();

            FieldsReplacement fieldsReplacement = new FieldsReplacement()
            {
                ReplacementIsMandatory = true,
                Fields = null,
            };

            var pushContactService = new Mock<IPushContactService>();
            var messageRepositoryMock = new Mock<IMessageRepository>();
            var messageServiceMock = new Mock<IMessageService>();
            var messageSenderMock = new Mock<IMessageSender>();
            var pushContactRepository = new Mock<IPushContactRepository>();

            messageServiceMock
                .Setup(x => x.GetMessageAsync(messageId))
                .ReturnsAsync((MessageDTO)null);

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(pushContactService.Object);
                    services.AddSingleton(messageRepositoryMock.Object);
                    services.AddSingleton(messageServiceMock.Object);
                    services.AddSingleton(messageSenderMock.Object);
                    services.AddSingleton(pushContactRepository.Object);
                });
            }).CreateClient(new WebApplicationFactoryClientOptions());

            var request = new HttpRequestMessage(HttpMethod.Post, $"messages/{messageId}/visitors/{visitorGuid}/send")
            {
                Headers = { { "Authorization", $"Bearer {TestApiUsersData.TOKEN_SUPERUSER_EXPIRE_20330518}" } },
                Content = JsonContent.Create(fieldsReplacement),
            };

            // Act
            var response = await client.SendAsync(request);
            _output.WriteLine(response.GetHeadersAsString());

            // Assert
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Theory]
        [InlineData("Title with fields: [[[field1]]] and [[[field2]]]", "Body without fields.")]
        [InlineData("Title without fields", "Body with fields: [[[field1]]] and [[[field2]]]")]
        public async Task ProcessWebPushForVisitorGuid_should_return_BadRequest_when_fields_are_missing_n_replacement_is_mandatory(string title, string body)
        {
            var fixture = new Fixture();

            // Arrange
            var messageId = fixture.Create<Guid>();
            var visitorGuid = fixture.Create<string>();

            var message = fixture.Create<MessageDTO>();
            message.MessageId = messageId;
            message.Title = title;
            message.Body = body;

            FieldsReplacement fieldsReplacement = new FieldsReplacement
            {
                ReplacementIsMandatory = true,
                Fields = new Dictionary<string, string>
                {
                    { "field1", "field1Value" },
                    { "field3", "field3Value" }, // it should be field2
                }
            };

            var pushContactService = new Mock<IPushContactService>();
            var messageRepositoryMock = new Mock<IMessageRepository>();
            var messageServiceMock = new Mock<IMessageService>();
            var messageSenderMock = new Mock<IMessageSender>();
            var pushContactRepository = new Mock<IPushContactRepository>();

            messageServiceMock
                .Setup(x => x.GetMessageAsync(messageId))
                .ReturnsAsync(message);

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(pushContactService.Object);
                    services.AddSingleton(messageRepositoryMock.Object);
                    services.AddSingleton(messageServiceMock.Object);
                    services.AddSingleton(messageSenderMock.Object);
                    services.AddSingleton(pushContactRepository.Object);
                });
            }).CreateClient(new WebApplicationFactoryClientOptions());

            var request = new HttpRequestMessage(HttpMethod.Post, $"messages/{messageId}/visitors/{visitorGuid}/send")
            {
                Headers = { { "Authorization", $"Bearer {TestApiUsersData.TOKEN_SUPERUSER_EXPIRE_20330518}" } },
                Content = JsonContent.Create(fieldsReplacement),
            };

            // Act
            var response = await client.SendAsync(request);
            var responseContent = await response.Content.ReadAsStringAsync();

            _output.WriteLine(response.GetHeadersAsString());
            _output.WriteLine(responseContent);

            // Assert
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            using var jsonDoc = JsonDocument.Parse(responseContent);
            var root = jsonDoc.RootElement;

            var error = root.GetProperty("error").GetString();
            var missingFieldsInTitle = root.GetProperty("missingFieldsInTitle").EnumerateArray().Select(x => x.GetString()).ToList();
            var missingFieldsInBody = root.GetProperty("missingFieldsInBody").EnumerateArray().Select(x => x.GetString()).ToList();

            var missingFields = missingFieldsInTitle.Concat(missingFieldsInBody);
            Assert.Equal("Missing replacements values in title or body.", error);
            Assert.Contains("field2", missingFields);
        }

        [Theory]
        [InlineData("Title with fields: [[[field1]]] and [[[field2]]]", "Body without fields.")]
        [InlineData("Title without fields", "Body with fields: [[[field1]]] and [[[field2]]]")]
        public async Task ProcessWebPushForVisitorGuid_should_return_Accepted_when_fields_are_missing_but_replacement_is_not_mandatory(string title, string body)
        {
            var fixture = new Fixture();

            // Arrange
            var messageId = fixture.Create<Guid>();
            var visitorGuid = fixture.Create<string>();

            var message = fixture.Create<MessageDTO>();
            message.MessageId = messageId;
            message.Title = title;
            message.Body = body;

            FieldsReplacement fieldsReplacement = new FieldsReplacement
            {
                ReplacementIsMandatory = false,
                Fields = new Dictionary<string, string>
                {
                    { "field1", "field1Value" },
                    { "field3", "field3Value" }, // it should be field2
                }
            };

            var pushContactService = new Mock<IPushContactService>();
            var messageRepositoryMock = new Mock<IMessageRepository>();
            var messageServiceMock = new Mock<IMessageService>();
            var messageSenderMock = new Mock<IMessageSender>();
            var pushContactRepository = new Mock<IPushContactRepository>();
            var webPushPublisherServiceMock = new Mock<IWebPushPublisherService>();

            messageServiceMock
                .Setup(x => x.GetMessageAsync(messageId))
                .ReturnsAsync(message);

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(pushContactService.Object);
                    services.AddSingleton(messageRepositoryMock.Object);
                    services.AddSingleton(messageServiceMock.Object);
                    services.AddSingleton(messageSenderMock.Object);
                    services.AddSingleton(pushContactRepository.Object);
                    services.AddSingleton(webPushPublisherServiceMock.Object);
                });
            }).CreateClient(new WebApplicationFactoryClientOptions());

            var request = new HttpRequestMessage(HttpMethod.Post, $"messages/{messageId}/visitors/{visitorGuid}/send")
            {
                Headers = { { "Authorization", $"Bearer {TestApiUsersData.TOKEN_SUPERUSER_EXPIRE_20330518}" } },
                Content = JsonContent.Create(fieldsReplacement),
            };

            // Act
            var response = await client.SendAsync(request);
            var responseContent = await response.Content.ReadAsStringAsync();

            _output.WriteLine(response.GetHeadersAsString());
            _output.WriteLine(responseContent);

            // Assert
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            webPushPublisherServiceMock.Verify(
                x => x.ProcessWebPushForVisitors(
                    It.IsAny<WebPushDTO>(),
                    It.IsAny<FieldsReplacementList>(),
                    It.IsAny<string>()),
                Times.Once
            );
        }

        [Fact]
        public async Task ProcessWebPushForVisitorGuid_should_return_InternalServerError_when_unexpected_error_happens()
        {
            var fixture = new Fixture();

            // Arrange
            var messageId = fixture.Create<Guid>();
            var visitorGuid = fixture.Create<string>();

            FieldsReplacement fieldsReplacement = new FieldsReplacement()
            {
                ReplacementIsMandatory = true,
                Fields = null,
            };

            var testException = new Exception("my exception on testing");

            var pushContactService = new Mock<IPushContactService>();
            var messageRepositoryMock = new Mock<IMessageRepository>();
            var messageServiceMock = new Mock<IMessageService>();
            var messageSenderMock = new Mock<IMessageSender>();
            var pushContactRepository = new Mock<IPushContactRepository>();
            var loggerMock = new Mock<ILogger<MessageController>>();

            messageServiceMock
                .Setup(x => x.GetMessageAsync(messageId))
                .Throws(testException);

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(pushContactService.Object);
                    services.AddSingleton(messageRepositoryMock.Object);
                    services.AddSingleton(messageServiceMock.Object);
                    services.AddSingleton(messageSenderMock.Object);
                    services.AddSingleton(pushContactRepository.Object);
                    services.AddSingleton(loggerMock.Object);
                });
            }).CreateClient(new WebApplicationFactoryClientOptions());

            var request = new HttpRequestMessage(HttpMethod.Post, $"messages/{messageId}/visitors/{visitorGuid}/send")
            {
                Headers = { { "Authorization", $"Bearer {TestApiUsersData.TOKEN_SUPERUSER_EXPIRE_20330518}" } },
                Content = JsonContent.Create(fieldsReplacement),
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
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains($"An unexpected error occurred processing web push")),
                    It.Is<Exception>(ex => ex == testException),
                    It.Is<Func<It.IsAnyType, Exception, string>>((v, t) => true)),
                Times.Once);
        }

        [Theory]
        [InlineData(TestApiUsersData.TOKEN_EMPTY)]
        [InlineData(TestApiUsersData.TOKEN_BROKEN)]
        public async Task ProcessWebPushForVisitors_should_return_unauthorized_when_token_is_not_valid(string token)
        {
            // Arrange
            var client = _factory.CreateClient(new WebApplicationFactoryClientOptions());

            var fixture = new Fixture();
            var messageId = fixture.Create<Guid>();

            var request = new HttpRequestMessage(HttpMethod.Post, $"messages/{messageId}/visitors/send")
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
        public async Task ProcessWebPushForVisitors_should_return_unauthorized_when_token_is_an_expired_superuser_token(string token)
        {
            // Arrange
            var client = _factory.CreateClient(new WebApplicationFactoryClientOptions());

            var fixture = new Fixture();
            var messageId = fixture.Create<Guid>();

            var request = new HttpRequestMessage(HttpMethod.Post, $"messages/{messageId}/visitors/send")
            {
                Headers = { { "Authorization", $"Bearer {token}" } }
            };

            // Act
            var response = await client.SendAsync(request);
            _output.WriteLine(response.GetHeadersAsString());

            // Assert
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task ProcessWebPushForVisitors_should_return_unauthorized_when_authorization_header_is_not_defined()
        {
            // Arrange
            var client = _factory.CreateClient(new WebApplicationFactoryClientOptions());

            var fixture = new Fixture();
            var messageId = fixture.Create<Guid>();

            var request = new HttpRequestMessage(HttpMethod.Post, $"messages/{messageId}/visitors/send");

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
        public async Task ProcessWebPushForVisitors_should_require_a_token_with_isSU_flag(string token)
        {
            // Arrange
            var client = _factory.CreateClient(new WebApplicationFactoryClientOptions());

            var fixture = new Fixture();
            var messageId = fixture.Create<Guid>();

            var request = new HttpRequestMessage(HttpMethod.Post, $"messages/{messageId}/visitors/send")
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
        public async Task ProcessWebPushForVisitors_should_return_NotFound_when_message_doesnot_exist()
        {
            var fixture = new Fixture();

            // Arrange
            var messageId = fixture.Create<Guid>();

            FieldsReplacementList fieldsReplacementList = new FieldsReplacementList()
            {
                ReplacementIsMandatory = true,
            };

            var pushContactService = new Mock<IPushContactService>();
            var messageRepositoryMock = new Mock<IMessageRepository>();
            var messageServiceMock = new Mock<IMessageService>();
            var messageSenderMock = new Mock<IMessageSender>();
            var pushContactRepository = new Mock<IPushContactRepository>();

            messageServiceMock
                .Setup(x => x.GetMessageAsync(messageId))
                .ReturnsAsync((MessageDTO)null);

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(pushContactService.Object);
                    services.AddSingleton(messageRepositoryMock.Object);
                    services.AddSingleton(messageServiceMock.Object);
                    services.AddSingleton(messageSenderMock.Object);
                    services.AddSingleton(pushContactRepository.Object);
                });
            }).CreateClient(new WebApplicationFactoryClientOptions());

            var request = new HttpRequestMessage(HttpMethod.Post, $"messages/{messageId}/visitors/send")
            {
                Headers = { { "Authorization", $"Bearer {TestApiUsersData.TOKEN_SUPERUSER_EXPIRE_20330518}" } },
                Content = JsonContent.Create(fieldsReplacementList),
            };

            // Act
            var response = await client.SendAsync(request);
            _output.WriteLine(response.GetHeadersAsString());

            // Assert
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Fact]
        public async Task ProcessWebPushForVisitors_should_return_BadRequest_when_VisitorsFieldsList_is_null()
        {
            var fixture = new Fixture();

            // Arrange
            var messageId = fixture.Create<Guid>();
            var message = fixture.Create<MessageDTO>();
            message.MessageId = messageId;

            FieldsReplacementList fieldsReplacementList = new FieldsReplacementList()
            {
                ReplacementIsMandatory = true,
                VisitorsFieldsList = null,
            };

            var pushContactService = new Mock<IPushContactService>();
            var messageRepositoryMock = new Mock<IMessageRepository>();
            var messageServiceMock = new Mock<IMessageService>();
            var messageSenderMock = new Mock<IMessageSender>();
            var pushContactRepository = new Mock<IPushContactRepository>();

            messageServiceMock
                .Setup(x => x.GetMessageAsync(messageId))
                .ReturnsAsync(message);

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(pushContactService.Object);
                    services.AddSingleton(messageRepositoryMock.Object);
                    services.AddSingleton(messageServiceMock.Object);
                    services.AddSingleton(messageSenderMock.Object);
                    services.AddSingleton(pushContactRepository.Object);
                });
            }).CreateClient(new WebApplicationFactoryClientOptions());

            var request = new HttpRequestMessage(HttpMethod.Post, $"messages/{messageId}/visitors/send")
            {
                Headers = { { "Authorization", $"Bearer {TestApiUsersData.TOKEN_SUPERUSER_EXPIRE_20330518}" } },
                Content = JsonContent.Create(fieldsReplacementList),
            };

            // Act
            var response = await client.SendAsync(request);
            _output.WriteLine(response.GetHeadersAsString());

            // Assert
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task ProcessWebPushForVisitors_should_return_Accepted()
        {
            var fixture = new Fixture();

            // Arrange
            var messageId = fixture.Create<Guid>();
            var visitorGuid = fixture.Create<string>();

            var message = fixture.Create<MessageDTO>();
            message.MessageId = messageId;

            var visitorFields1 = new VisitorFields
            {
                VisitorGuid = Guid.NewGuid().ToString(),
                Fields = new Dictionary<string, string> { { "field1", "value1" } }
            };

            var visitorsFieldsList = new List<VisitorFields> { visitorFields1 };

            FieldsReplacementList fieldsReplacementList = new FieldsReplacementList
            {
                ReplacementIsMandatory = false,
                VisitorsFieldsList = visitorsFieldsList,
            };

            var pushContactService = new Mock<IPushContactService>();
            var messageRepositoryMock = new Mock<IMessageRepository>();
            var messageServiceMock = new Mock<IMessageService>();
            var messageSenderMock = new Mock<IMessageSender>();
            var pushContactRepository = new Mock<IPushContactRepository>();
            var webPushPublisherServiceMock = new Mock<IWebPushPublisherService>();

            messageServiceMock
                .Setup(x => x.GetMessageAsync(messageId))
                .ReturnsAsync(message);

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(pushContactService.Object);
                    services.AddSingleton(messageRepositoryMock.Object);
                    services.AddSingleton(messageServiceMock.Object);
                    services.AddSingleton(messageSenderMock.Object);
                    services.AddSingleton(pushContactRepository.Object);
                    services.AddSingleton(webPushPublisherServiceMock.Object);
                });
            }).CreateClient(new WebApplicationFactoryClientOptions());

            var request = new HttpRequestMessage(HttpMethod.Post, $"messages/{messageId}/visitors/send")
            {
                Headers = { { "Authorization", $"Bearer {TestApiUsersData.TOKEN_SUPERUSER_EXPIRE_20330518}" } },
                Content = JsonContent.Create(fieldsReplacementList),
            };

            // Act
            var response = await client.SendAsync(request);
            var responseContent = await response.Content.ReadAsStringAsync();

            _output.WriteLine(response.GetHeadersAsString());
            _output.WriteLine(responseContent);

            // Assert
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            webPushPublisherServiceMock.Verify(
                x => x.ProcessWebPushForVisitors(
                    It.IsAny<WebPushDTO>(),
                    It.IsAny<FieldsReplacementList>(),
                    It.IsAny<string>()),
                Times.Once
            );
        }

        [Fact]
        public async Task ProcessWebPushForVisitors_should_return_InternalServerError_when_unexpected_error_happens()
        {
            var fixture = new Fixture();

            // Arrange
            var messageId = fixture.Create<Guid>();

            var visitorFields1 = new VisitorFields
            {
                VisitorGuid = Guid.NewGuid().ToString(),
                Fields = new Dictionary<string, string> { { "field1", "value1" } }
            };

            var visitorsFieldsList = new List<VisitorFields> { visitorFields1 };

            FieldsReplacementList fieldsReplacementList = new FieldsReplacementList
            {
                ReplacementIsMandatory = false,
                VisitorsFieldsList = visitorsFieldsList,
            };

            var testException = new Exception("my exception on testing");

            var pushContactService = new Mock<IPushContactService>();
            var messageRepositoryMock = new Mock<IMessageRepository>();
            var messageServiceMock = new Mock<IMessageService>();
            var messageSenderMock = new Mock<IMessageSender>();
            var pushContactRepository = new Mock<IPushContactRepository>();
            var loggerMock = new Mock<ILogger<MessageController>>();

            messageServiceMock
                .Setup(x => x.GetMessageAsync(messageId))
                .Throws(testException);

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(pushContactService.Object);
                    services.AddSingleton(messageRepositoryMock.Object);
                    services.AddSingleton(messageServiceMock.Object);
                    services.AddSingleton(messageSenderMock.Object);
                    services.AddSingleton(pushContactRepository.Object);
                    services.AddSingleton(loggerMock.Object);
                });
            }).CreateClient(new WebApplicationFactoryClientOptions());

            var request = new HttpRequestMessage(HttpMethod.Post, $"messages/{messageId}/visitors/send")
            {
                Headers = { { "Authorization", $"Bearer {TestApiUsersData.TOKEN_SUPERUSER_EXPIRE_20330518}" } },
                Content = JsonContent.Create(fieldsReplacementList),
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
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains($"An unexpected error occurred processing web push")),
                    It.Is<Exception>(ex => ex == testException),
                    It.Is<Func<It.IsAnyType, Exception, string>>((v, t) => true)),
                Times.Once);
        }
    }
}
