using AutoFixture;
using Doppler.PushContact.Models;
using Doppler.PushContact.Models.DTOs;
using Doppler.PushContact.Models.Entities;
using Doppler.PushContact.Repositories.Interfaces;
using Doppler.PushContact.Services;
using Doppler.PushContact.Services.Messages;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Doppler.PushContact.Test.Services.Messages
{
    public class MessageServiceTest
    {
        private static MessageService CreateSut(
            IMessageRepository messageRepository = null,
            ILogger<MessageService> logger = null
        )
        {
            return new MessageService(
                messageRepository ?? Mock.Of<IMessageRepository>(),
                logger ?? Mock.Of<ILogger<MessageService>>()
            );
        }

        [Fact]
        public async Task GetMessageAsync_should_return_message_OK()
        {
            // Arrange
            var fixture = new Fixture();
            var messageId = fixture.Create<Guid>();

            var messageDetails = new MessageDetails()
            {
                MessageId = messageId,
                Title = fixture.Create<string>(),
                Body = fixture.Create<string>(),
                Actions = new List<MessageAction>()
                {
                    new MessageAction()
                    {
                        Action = "Action1",
                        Title = "Title1",
                        Icon = "https://icon1.png",
                        Link = "https://link1.com",
                    },
                }
            };

            var messageRepositoryMock = new Mock<IMessageRepository>();

            messageRepositoryMock
                .Setup(x => x.GetMessageDetailsByMessageIdAsync(messageId))
                .ReturnsAsync(messageDetails);

            var sut = CreateSut(
                messageRepository: messageRepositoryMock.Object
            );

            // Act
            var messageDto = await sut.GetMessageAsync(messageId);

            // Assert
            Assert.Equal(messageDetails.MessageId, messageDto.MessageId);
            Assert.Equal(messageDetails.Title, messageDto.Title);
            Assert.Equal(messageDetails.Body, messageDto.Body);

            Assert.Equal(messageDetails.Actions.Count, messageDto.Actions.Count);

            foreach (var actionDetail in messageDetails.Actions)
            {
                var actionDto = messageDto.Actions.SingleOrDefault(a => a.Action == actionDetail.Action);
                Assert.NotNull(actionDto);
                Assert.Equal(actionDetail.Title, actionDto.Title);
                Assert.Equal(actionDetail.Icon, actionDto.Icon);
                Assert.Equal(actionDetail.Link, actionDto.Link);
            }
        }

        [Fact]
        public async Task GetMessageAsync_should_return_null_when_messagerepository_returns_null()
        {
            // Arrange
            var fixture = new Fixture();
            var messageId = fixture.Create<Guid>();

            var messageRepositoryMock = new Mock<IMessageRepository>();

            messageRepositoryMock
                .Setup(x => x.GetMessageDetailsByMessageIdAsync(messageId))
                .ReturnsAsync((MessageDetails)null);

            var sut = CreateSut(
                messageRepository: messageRepositoryMock.Object
            );

            // Act
            var messageDto = await sut.GetMessageAsync(messageId);

            // Assert
            Assert.Null(messageDto);
        }

        [Theory]
        [InlineData("http://without_https.com")]
        [InlineData("withoutprotocol.com")]
        [InlineData("//not/absolute/url.com")]
        [InlineData("https:invalidurl.com")]
        [InlineData("https://invalidurl.com<>")]
        public async Task AddMessageAsync_should_return_argumentexception_when_OnClickLink_is_invalid(string onClickLink)
        {
            // Arrange
            var fixture = new Fixture();
            var messageId = fixture.Create<Guid>();

            var messageDto = new MessageDTO()
            {
                MessageId = messageId,
                Title = fixture.Create<string>(),
                Body = fixture.Create<string>(),
                OnClickLink = onClickLink,
            };

            var messageRepositoryMock = new Mock<IMessageRepository>();

            var sut = CreateSut(
                messageRepository: messageRepositoryMock.Object
            );

            // Act
            // Assert
            await Assert.ThrowsAsync<ArgumentException>(() => sut.AddMessageAsync(messageDto));
        }

        [Theory]
        [InlineData("http://without_https.com/test-image.png")]
        [InlineData("withoutprotocol.com/test-image.png")]
        [InlineData("//not/absolute/url.com/test-image.png")]
        [InlineData("https:invalidurl.com/test-image.png")]
        [InlineData("https://invalidurl.com<>/test-image.png")]
        public async Task AddMessageAsync_should_return_argumentexception_when_ImageUrl_is_invalid(string imageUrl)
        {
            // Arrange
            var fixture = new Fixture();
            var messageId = fixture.Create<Guid>();

            var messageDto = new MessageDTO()
            {
                MessageId = messageId,
                Title = fixture.Create<string>(),
                Body = fixture.Create<string>(),
                ImageUrl = imageUrl,
            };

            var messageRepositoryMock = new Mock<IMessageRepository>();

            var sut = CreateSut(
                messageRepository: messageRepositoryMock.Object
            );

            // Act
            // Assert
            await Assert.ThrowsAsync<ArgumentException>(() => sut.AddMessageAsync(messageDto));
        }

        [Fact]
        public async Task AddMessageAsync_should_finish_Ok_when_ImageUrl_and_OnClickLink_are_valid()
        {
            // Arrange
            var fixture = new Fixture();
            var messageId = fixture.Create<Guid>();

            var actions = new List<MessageActionDTO>()
            {
                new MessageActionDTO()
                {
                    Action = "My action One",
                    Title = "My title One",
                    Icon = "https://icon1.png",
                    Link = "https://link1.com",
                },
            };

            var messageDto = new MessageDTO()
            {
                MessageId = messageId,
                Domain = fixture.Create<string>(),
                Title = fixture.Create<string>(),
                Body = fixture.Create<string>(),
                ImageUrl = "https://good.image.com/goodImage.png",
                OnClickLink = "https://good.link.com",
                Actions = actions,
            };

            var expectedSanitizedAction1 = new MessageActionDTO()
            {
                Action = "My action One",
                Title = "My title One",
                Icon = "https://icon1.png",
                Link = "https://link1.com",
            };

            var messageRepositoryMock = new Mock<IMessageRepository>();
            messageRepositoryMock
                .Setup(x => x.AddAsync(messageDto))
            .Returns(Task.CompletedTask);

            var sut = CreateSut(
                messageRepository: messageRepositoryMock.Object
            );

            // Act
            await sut.AddMessageAsync(messageDto);

            // Assert
            messageRepositoryMock.Verify(x => x.AddAsync(messageDto), Times.Once);
        }

        [Fact]
        public async Task AddMessageAsync_should_sanitize_actions_OK_when_action_and_action_title_are_undefined()
        {
            // Arrange
            var fixture = new Fixture();
            var messageId = fixture.Create<Guid>();

            var actions = new List<MessageActionDTO>()
            {
                new MessageActionDTO()
                {
                    Action = "",
                    Title = "",
                    Icon = "https://icon1.png",
                    Link = "https://link1.com",
                },
                new MessageActionDTO()
                {
                    Icon = "https://icon2.png",
                    Link = "https://link2.com",
                },
            };

            var expectedSanitizedAction1 = new MessageActionDTO()
            {
                Action = "action1",
                Title = "title1",
                Icon = "https://icon1.png",
                Link = "https://link1.com",
            };

            var expectedSanitizedAction2 = new MessageActionDTO()
            {
                Action = "action2",
                Title = "title2",
                Icon = "https://icon2.png",
                Link = "https://link2.com",
            };

            var messageDto = new MessageDTO()
            {
                MessageId = messageId,
                Domain = fixture.Create<string>(),
                Title = fixture.Create<string>(),
                Body = fixture.Create<string>(),
                ImageUrl = "https://good.image.com/goodImage.png",
                OnClickLink = "https://good.link.com",
                Actions = actions,
            };

            var messageRepositoryMock = new Mock<IMessageRepository>();
            messageRepositoryMock
                .Setup(x => x.AddAsync(messageDto))
            .Returns(Task.CompletedTask);

            var sut = CreateSut(
                messageRepository: messageRepositoryMock.Object
            );

            // Act
            await sut.AddMessageAsync(messageDto);

            // Assert
            messageRepositoryMock.Verify(x => x.AddAsync(messageDto), Times.Once);
        }

        [Fact]
        public async Task AddMessageAsync_should_return_argumentnullexception_when_messageDTO_is_null()
        {
            // Arrange
            var messageRepositoryMock = new Mock<IMessageRepository>();

            var sut = CreateSut(
                messageRepository: messageRepositoryMock.Object
            );

            // Act
            // Assert
            await Assert.ThrowsAsync<ArgumentNullException>(() => sut.AddMessageAsync(null));
        }

        [Theory]
        [InlineData(null, "title", "body")]
        [InlineData("", "title", "body")]
        [InlineData("domain.com", null, "body")]
        [InlineData("domain.com", "", "body")]
        [InlineData("domain.com", "title", null)]
        [InlineData("domain.com", "title", "")]
        public async Task AddMessageAsync_should_return_argumentexception_when_Domain_Title_Body_are_null_or_empty(string domain, string title, string body)
        {
            // Arrange
            var fixture = new Fixture();
            var messageId = fixture.Create<Guid>();

            var messageDto = new MessageDTO()
            {
                MessageId = messageId,
                Title = title,
                Body = body,
                Domain = domain,
            };

            var messageRepositoryMock = new Mock<IMessageRepository>();

            var sut = CreateSut(
                messageRepository: messageRepositoryMock.Object
            );

            // Act
            // Assert
            await Assert.ThrowsAsync<ArgumentException>(() => sut.AddMessageAsync(messageDto));
        }
    }
}
