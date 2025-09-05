using AutoFixture;
using Doppler.PushContact.Models.DTOs;
using Doppler.PushContact.Services;
using Doppler.PushContact.Services.Messages;
using Doppler.PushContact.Services.Messages.ExternalContracts;
using Flurl.Http;
using Flurl.Http.Testing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;

namespace Doppler.PushContact.Test.Services.Messages
{
    public class MessageSenderTest
    {
        private static readonly MessageSenderSettings messageSenderSettingsDefault =
            new MessageSenderSettings
            {
                PushApiUrl = "https://localhost:9999",
                FatalMessagingErrorCodes = new List<int>() { 1, 2, 3, 4 },
                PushTokensLimit = 400
            };

        private static MessageSender CreateSut(
            IPushApiTokenGetter pushApiTokenGetter = null,
            IOptions<MessageSenderSettings> messageSenderSettings = null,
            IMessageRepository messageRepository = null,
            IPushContactService pushContactService = null,
            ILogger<MessageSender> logger = null
        )
        {
            return new MessageSender(
                messageSenderSettings ?? Options.Create(messageSenderSettingsDefault),
                pushApiTokenGetter ?? Mock.Of<IPushApiTokenGetter>(),
                messageRepository ?? Mock.Of<IMessageRepository>(),
                pushContactService ?? Mock.Of<IPushContactService>(),
                logger ?? Mock.Of<ILogger<MessageSender>>()
            );
        }

        [Theory]
        [InlineData(null, "some body", new string[] { "someTargetDeviceTokens" })]
        [InlineData("some title", null, new string[] { "someTargetDeviceTokens" })]
        [InlineData("some title", "some body", null)]
        [InlineData(null, null, null)]
        [InlineData("some title", "", new string[] { "someTargetDeviceTokens" })]
        [InlineData("", "some body", new string[] { "someTargetDeviceTokens" })]
        [InlineData("some title", "some body", new string[] { })]
        [InlineData("", "", new string[] { })]
        public async Task
            SendAsync_should_throw_argument_exception_and_not_have_made_a_http_call_when_title_or_body_or_target_are_null_or_empty(string title, string body, string[] targetDeviceTokens)
        {
            // Arrange
            using var httpTest = new HttpTest();

            var sut = CreateSut();

            // Act
            // Assert
            await Assert.ThrowsAsync<ArgumentException>(() => sut.SendAsync(title, body, targetDeviceTokens));
            httpTest.ShouldNotHaveMadeACall();
        }

        [Theory]
        [InlineData(500)]
        [InlineData(400)]
        [InlineData(401)]
        [InlineData(404)]
        public async Task SendAsync_should_throw_flurl_http_exception_when_push_api_response_a_not_success_status(int notSuccessStatus)
        {
            // Arrange
            var fixture = new Fixture();

            var title = fixture.Create<string>();
            var body = fixture.Create<string>();
            var targetDeviceTokens = fixture.CreateMany<string>();

            using var httpTest = new HttpTest();

            httpTest.RespondWithJson(string.Empty, notSuccessStatus);

            var sut = CreateSut();

            // Act
            // Assert
            await Assert.ThrowsAsync<FlurlHttpException>(() => sut.SendAsync(title, body, targetDeviceTokens));
            httpTest.ShouldHaveCalled($"{messageSenderSettingsDefault.PushApiUrl}/message")
                .WithVerb(HttpMethod.Post)
                .Times(1);
        }

        [Fact(Skip = "Now, the pushApiTokenGetter should not be used.")]
        public async Task SendAsync_should_throw_exception_and_does_not_call_push_api_when_push_api_token_getter_throw_exception()
        {
            // Arrange
            var fixture = new Fixture();

            var title = fixture.Create<string>();
            var body = fixture.Create<string>();
            var targetDeviceTokens = fixture.CreateMany<string>();

            using var httpTest = new HttpTest();

            var pushApiTokenGetterMock = new Mock<IPushApiTokenGetter>();
            pushApiTokenGetterMock
                .Setup(x => x.GetTokenAsync())
                .ThrowsAsync(new Exception());

            var sut = CreateSut(
                pushApiTokenGetter: pushApiTokenGetterMock.Object
                );

            // Act
            // Assert
            await Assert.ThrowsAsync<Exception>(() => sut.SendAsync(title, body, targetDeviceTokens));
            httpTest.ShouldNotHaveMadeACall();
        }

        [Fact]
        public async Task SendAsync_should_return_only_one_message_target_result_per_push_api_send_message_response()
        {
            // Arrange
            var fixture = new Fixture();

            var title = fixture.Create<string>();
            var body = fixture.Create<string>();
            var targetDeviceTokens = fixture.CreateMany<string>();

            var sendMessageResponse = fixture.Create<SendMessageResponse>();

            using var httpTest = new HttpTest();
            httpTest.RespondWithJson(sendMessageResponse, 200);

            var sut = CreateSut();

            // Act
            var sendMessageResult = await sut.SendAsync(title, body, targetDeviceTokens);

            // Assert
            Assert.All(sendMessageResponse.Responses, x => sendMessageResult.SendMessageTargetResult.Single(y => y.TargetDeviceToken == x.DeviceToken));
            httpTest.ShouldHaveCalled($"{messageSenderSettingsDefault.PushApiUrl}/message")
                .WithVerb(HttpMethod.Post)
                .Times(1);
        }

        [Theory]
        [InlineData("http://urlwithhttpschema.com/random-resource.img")]
        [InlineData("urlwithoutschema.com/random-resource.img")]
        [InlineData("//not/absolute/url/random-resource.img")]
        [InlineData("https:invalidurl.com/random-resource.img")]
        [InlineData("https://invalidurl.com<>/random-resource.img")]
        public async Task
            SendAsync_should_throw_argument_exception_and_not_have_made_a_http_call_when_onClickLink_is_a_not_valid_url(string notValidOnClickLink)
        {
            // Arrange
            var fixture = new Fixture();

            var title = fixture.Create<string>();
            var body = fixture.Create<string>();
            var targetDeviceTokens = fixture.CreateMany<string>();

            using var httpTest = new HttpTest();

            var sut = CreateSut();

            // Act
            // Assert
            await Assert.ThrowsAsync<ArgumentException>(() => sut.SendAsync(title, body, targetDeviceTokens, notValidOnClickLink));
            httpTest.ShouldNotHaveMadeACall();
        }

        [Theory]
        [InlineData("https://valid-domain.com/")]
        [InlineData("https://valid-domain.com")]
        [InlineData("https://valid-domain.com/random-resource.img")]
        [InlineData("https://www.valid-domain.com/random-resource.img")]
        [InlineData("https://valid-domain.com/random-resource.img?someParam=paramValue&otherParam=otherValue")]
        [InlineData("https://valid-domain.com/random-resource.img#L6")]
        public async Task
            SendAsync_should_does_not_throw_exception_and_have_made_a_http_call_when_all_params_are_valid(string validOnClickLink)
        {
            // Arrange
            var fixture = new Fixture();

            var validTitle = "Valid title";
            var validBody = "Valid body";
            var validTargetDeviceTokens = new string[] { "someTargetDeviceToken" };

            var sendMessageResponse = fixture.Create<SendMessageResponse>();

            using var httpTest = new HttpTest();
            httpTest.RespondWithJson(sendMessageResponse, 200);

            var sut = CreateSut();

            // Act
            await sut.SendAsync(validTitle, validBody, validTargetDeviceTokens, validOnClickLink);

            // Assert
            httpTest.ShouldHaveCalled($"{messageSenderSettingsDefault.PushApiUrl}/message")
                .WithVerb(HttpMethod.Post)
                .Times(1);
        }

        [Theory]
        [InlineData("http://urlwithhttpschema.com/random-resource.img")]
        [InlineData("urlwithoutschema.com/random-resource.img")]
        [InlineData("//not/absolute/url/random-resource.img")]
        [InlineData("https:invalidurl.com/random-resource.img")]
        [InlineData("https://invalidurl.com<>/random-resource.img")]
        public async Task
            SendAsync_should_throw_argument_exception_and_not_have_made_a_http_call_when_imageUrl_is_a_not_valid_url(string notValidimageUrl)
        {
            // Arrange
            var fixture = new Fixture();

            var title = fixture.Create<string>();
            var body = fixture.Create<string>();
            var targetDeviceTokens = fixture.CreateMany<string>();

            using var httpTest = new HttpTest();

            var sut = CreateSut();

            // Act
            // Assert
            await Assert.ThrowsAsync<ArgumentException>(() => sut.SendAsync(title, body, targetDeviceTokens, imageUrl: notValidimageUrl));
            httpTest.ShouldNotHaveMadeACall();
        }

        [Theory]
        [InlineData("https://i.ibb.co/yNhZqqt/exampleimage.jpg")]
        public async Task
            SendAsync_should_does_not_throw_exception_and_have_made_a_http_call_when_imageUrl_is_valid(string validImageUrl)
        {
            // Arrange
            var fixture = new Fixture();

            var validTitle = "Valid title";
            var validBody = "Valid body";
            var validTargetDeviceTokens = new string[] { "someTargetDeviceToken" };

            var sendMessageResponse = fixture.Create<SendMessageResponse>();

            using var httpTest = new HttpTest();
            httpTest.RespondWithJson(sendMessageResponse, 200);

            var sut = CreateSut();

            // Act
            await sut.SendAsync(validTitle, validBody, validTargetDeviceTokens, imageUrl: validImageUrl);

            // Assert
            httpTest.ShouldHaveCalled($"{messageSenderSettingsDefault.PushApiUrl}/message")
                .WithVerb(HttpMethod.Post)
                .Times(1);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public async Task AddMessageAsync_should_throw_argument_exception_when_title_is_null_or_empty(string title)
        {
            // Arrange
            var fixture = new Fixture();
            var domain = fixture.Create<string>();
            var body = fixture.Create<string>();

            var sut = CreateSut();

            // Act
            var exception = await Assert.ThrowsAsync<ArgumentException>(() => sut.AddMessageAsync(domain, title, body, null, null));

            // Assert
            Assert.Contains($"'title' cannot be null or empty.", exception.Message);
            Assert.Equal("title", exception.ParamName);

        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public async Task AddMessageAsync_should_throw_argument_exception_when_body_is_null_or_empty(string body)
        {
            // Arrange
            var fixture = new Fixture();
            var domain = fixture.Create<string>();
            var title = fixture.Create<string>();

            var sut = CreateSut();

            // Act
            var exception = await Assert.ThrowsAsync<ArgumentException>(() => sut.AddMessageAsync(domain, title, body, null, null));

            // Assert
            Assert.Contains($"'body' cannot be null or empty.", exception.Message);
            Assert.Equal("body", exception.ParamName);

        }

        [Theory]
        [InlineData("http://urlwithhttpschema.com")]
        [InlineData("urlwithoutschema.com")]
        [InlineData("//not/absolute/url")]
        [InlineData("https:invalidurl.com")]
        [InlineData("https://invalidurl.com<>")]
        public async Task AddMessageAsync_should_throw_argument_exception_when_onClickLink_isnt_valid_url(string onClickLink)
        {
            // Arrange
            var fixture = new Fixture();
            var domain = fixture.Create<string>();
            var title = fixture.Create<string>();
            var body = fixture.Create<string>();
            var imageUrl = "https://www.mydomain.com/myImage.jpg";

            var sut = CreateSut();

            // Act
            var exception = await Assert.ThrowsAsync<ArgumentException>(() => sut.AddMessageAsync(domain, title, body, onClickLink, imageUrl));

            // Assert
            Assert.Contains($"'onClickLink' must be an absolute URL with HTTPS scheme.", exception.Message);
            Assert.Equal("onClickLink", exception.ParamName);
        }

        [Theory]
        [InlineData("http://urlwithhttpschema.com/random-resource.img")]
        [InlineData("urlwithoutschema.com/random-resource.img")]
        [InlineData("//not/absolute/url/random-resource.img")]
        [InlineData("https:invalidurl.com/random-resource.img")]
        [InlineData("https://invalidurl.com<>/random-resource.img")]
        public async Task AddMessageAsync_should_throw_argument_exception_when_imageUrl_isnt_valid_url(string imageUrl)
        {
            // Arrange
            var fixture = new Fixture();
            var domain = fixture.Create<string>();
            var title = fixture.Create<string>();
            var body = fixture.Create<string>();
            var onClickLink = "https://www.mydomain.com";

            var sut = CreateSut();

            // Act
            var exception = await Assert.ThrowsAsync<ArgumentException>(() => sut.AddMessageAsync(domain, title, body, onClickLink, imageUrl));

            // Assert
            Assert.Contains($"'imageUrl' must be an absolute URL with HTTPS scheme.", exception.Message);
            Assert.Equal("imageUrl", exception.ParamName);
        }

        [Fact]
        public async Task AddMessageAsync_should_return_a_valid_guid()
        {
            // Arrange
            var fixture = new Fixture();
            var domain = fixture.Create<string>();
            var title = fixture.Create<string>();
            var body = fixture.Create<string>();
            var onClickLink = "https://www.mydomain.com";
            var imageUrl = "https://www.mydomain.com/myImage.jpg";

            var sut = CreateSut();

            // Act
            var result = await sut.AddMessageAsync(domain, title, body, onClickLink, imageUrl);

            // Assert
            Assert.IsType<Guid>(result);
            Assert.NotEqual(Guid.Empty, result);
        }

        public static IEnumerable<object[]> InvalidTargetDeviceTokens()
        {
            yield return new object[] { null };
            yield return new object[] { new List<string>() };
        }

        [Theory]
        [MemberData(nameof(InvalidTargetDeviceTokens))]
        public async Task SendFirebaseWebPushAsync_should_finish_without_call_to_services_when_devicetokens_is_empty_or_null(List<string> deviceTokens)
        {
            // Arrange
            var fixture = new Fixture();
            var webPushDTO = new WebPushDTO()
            {
                Title = fixture.Create<string>(),
                Body = fixture.Create<string>(),
                MessageId = fixture.Create<Guid>(),
            };

            var mockPushApiTokenGetter = new Mock<IPushApiTokenGetter>();
            var mockMessageRepository = new Mock<IMessageRepository>();
            var mockPushContactService = new Mock<IPushContactService>();

            using var httpTest = new HttpTest();
            var sut = CreateSut(
                pushApiTokenGetter: mockPushApiTokenGetter.Object,
                messageRepository: mockMessageRepository.Object,
                pushContactService: mockPushContactService.Object
            );

            // Act
            await sut.SendFirebaseWebPushAsync(webPushDTO, deviceTokens, null);

            // Assert
            // verify that none the involved services were called
            mockPushApiTokenGetter.Verify(x => x.GetTokenAsync(), Times.Never);
            mockMessageRepository.Verify(x => x.UpdateDeliveriesAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);
            mockPushContactService.Verify(x => x.AddHistoryEventsAndMarkDeletedContactsAsync(It.IsAny<Guid>(), It.IsAny<SendMessageResult>()), Times.Never);
            httpTest.ShouldNotHaveMadeACall();
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public async Task SendFirebaseWebPushAsync_should_catch_argumentexception_and_logging_error_when_title_is_null_or_empty(string title)
        {
            // Arrange
            var fixture = new Fixture();
            var webPushDTO = new WebPushDTO()
            {
                Title = title,
                Body = fixture.Create<string>(),
                MessageId = fixture.Create<Guid>(),
            };

            var deviceTokens = new List<string> { fixture.Create<string>() };

            var loggerMock = new Mock<ILogger<MessageSender>>();

            using var httpTest = new HttpTest();
            var sut = CreateSut(
                logger: loggerMock.Object
            );

            // Act
            await sut.SendFirebaseWebPushAsync(webPushDTO, deviceTokens, null);

            // Assert
            httpTest.ShouldNotHaveMadeACall();
            loggerMock.Verify(
                x => x.Log(
                    It.Is<LogLevel>(l => l == LogLevel.Error),
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("An error occurred sending webpush using Firebase for messageId:")),
                    It.IsAny<ArgumentException>(),
                    It.Is<Func<It.IsAnyType, Exception, string>>((v, t) => true)),
                Times.Once);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public async Task SendFirebaseWebPushAsync_should_catch_argumentexception_and_logging_error_when_body_is_null_or_empty(string body)
        {
            // Arrange
            var fixture = new Fixture();
            var webPushDTO = new WebPushDTO()
            {
                Title = fixture.Create<string>(),
                Body = body,
                MessageId = fixture.Create<Guid>(),
            };

            var deviceTokens = new List<string> { fixture.Create<string>() };

            var loggerMock = new Mock<ILogger<MessageSender>>();

            using var httpTest = new HttpTest();
            var sut = CreateSut(
                logger: loggerMock.Object
            );

            // Act
            await sut.SendFirebaseWebPushAsync(webPushDTO, deviceTokens, null);

            // Assert
            httpTest.ShouldNotHaveMadeACall();
            loggerMock.Verify(
                x => x.Log(
                    It.Is<LogLevel>(l => l == LogLevel.Error),
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("An error occurred sending webpush using Firebase for messageId:")),
                    It.IsAny<ArgumentException>(),
                    It.Is<Func<It.IsAnyType, Exception, string>>((v, t) => true)),
                Times.Once);
        }

        [Fact]
        public async Task SendFirebaseWebPushAsync_should_call_to_the_services_related_to_RegisterStatistics_once_SendAsync_finished_ok()
        {
            // Arrange
            var fixture = new Fixture();
            var webPushDTO = new WebPushDTO()
            {
                Title = fixture.Create<string>(),
                Body = fixture.Create<string>(),
                MessageId = fixture.Create<Guid>(),
            };
            var authenticationApiToken = fixture.Create<string>();

            var deviceTokens = new List<string> { fixture.Create<string>() };

            var sendMessageResponse = fixture.Create<SendMessageResponse>();

            var mockMessageRepository = new Mock<IMessageRepository>();
            var mockPushContactService = new Mock<IPushContactService>();

            using var httpTest = new HttpTest();
            httpTest.RespondWithJson(sendMessageResponse, 200);

            var sut = CreateSut(
                messageRepository: mockMessageRepository.Object,
                pushContactService: mockPushContactService.Object
            );

            // Act
            await sut.SendFirebaseWebPushAsync(webPushDTO, deviceTokens, authenticationApiToken);

            // Assert
            httpTest.ShouldHaveCalled($"{messageSenderSettingsDefault.PushApiUrl}/message")
                .WithVerb(HttpMethod.Post)
                .Times(1);
            mockPushContactService.Verify(x => x.AddHistoryEventsAndMarkDeletedContactsAsync(webPushDTO.MessageId, It.IsAny<SendMessageResult>()), Times.Once);
            mockMessageRepository.Verify(x => x.UpdateDeliveriesAsync(webPushDTO.MessageId, It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()), Times.Once);
        }

        [Fact]
        public async Task SendFirebaseWebPushAsync_should_catch_exception_and_logging_error_when_unexpected_error_ocurr()
        {
            // Arrange
            var fixture = new Fixture();
            var webPushDTO = new WebPushDTO()
            {
                Title = fixture.Create<string>(),
                Body = fixture.Create<string>(),
                MessageId = fixture.Create<Guid>(),
            };

            var deviceTokens = new List<string> { fixture.Create<string>() };

            var loggerMock = new Mock<ILogger<MessageSender>>();

            var pushContactServiceMock = new Mock<IPushContactService>();
            pushContactServiceMock
                .Setup(x => x.AddHistoryEventsAndMarkDeletedContactsAsync(webPushDTO.MessageId, It.IsAny<SendMessageResult>()))
                .ThrowsAsync(new Exception());

            var sendMessageResponse = fixture.Create<SendMessageResponse>();
            using var httpTest = new HttpTest();
            httpTest.RespondWithJson(sendMessageResponse, 200);

            var sut = CreateSut(
                pushContactService: pushContactServiceMock.Object,
                logger: loggerMock.Object
            );

            // Act
            await sut.SendFirebaseWebPushAsync(webPushDTO, deviceTokens, null);

            // Assert
            loggerMock.Verify(
                x => x.Log(
                    It.Is<LogLevel>(l => l == LogLevel.Error),
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("An unexpected error occurred sending webpush using Firebase for messageId:")),
                    It.IsAny<Exception>(),
                    It.Is<Func<It.IsAnyType, Exception, string>>((v, t) => true)),
                Times.Once);
        }
    }
}
