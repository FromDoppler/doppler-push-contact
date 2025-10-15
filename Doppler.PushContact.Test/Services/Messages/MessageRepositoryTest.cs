using AutoFixture;
using Doppler.PushContact.Models.DTOs;
using Doppler.PushContact.Models.Entities;
using Doppler.PushContact.Models.Enums;
using Doppler.PushContact.Services;
using Doppler.PushContact.Services.Messages;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Doppler.PushContact.Test.Services.Messages
{
    public class MessageRepositoryTest
    {
        private static MessageRepository CreateSut(
            IMongoCollection<BsonDocument> mongoColletion = null,
            ILogger<MessageRepository> logger = null
        )
        {
            var settings = Options.Create(new PushMongoContextSettings
            {
                DatabaseName = "TestDatabase",
                MessagesCollectionName = "TestCollection"
            });

            var databaseMock = new Mock<IMongoDatabase>();
            databaseMock
                .Setup(x => x.GetCollection<BsonDocument>(settings.Value.MessagesCollectionName, null))
                .Returns(mongoColletion);

            var mongoClientMock = new Mock<IMongoClient>();
            mongoClientMock
                .Setup(x => x.GetDatabase(settings.Value.DatabaseName, null))
                .Returns(databaseMock.Object);

            return new MessageRepository(
                mongoClientMock.Object,
                settings,
                logger ?? Mock.Of<ILogger<MessageRepository>>());
        }

        [Fact]
        public async Task GetMessageDomainAsync_should_return_null_when_message_does_not_exist()
        {
            // Arrange
            var fixture = new Fixture();
            var messageId = fixture.Create<Guid>();

            var collectionMock = new Mock<IMongoCollection<BsonDocument>>();
            var asyncCursorMock = new Mock<IAsyncCursor<BsonDocument>>();

            // Configure the cursor to return no elements
            asyncCursorMock
                .SetupSequence(x => x.MoveNextAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);

            asyncCursorMock
                .SetupGet(x => x.Current)
                .Returns(new List<BsonDocument>());

            collectionMock
                .Setup(x => x.FindAsync(It.IsAny<FilterDefinition<BsonDocument>>(), It.IsAny<FindOptions<BsonDocument, BsonDocument>>(), default))
                .ReturnsAsync(asyncCursorMock.Object);

            var sut = CreateSut(collectionMock.Object);

            // Act
            var result = await sut.GetMessageDomainAsync(messageId);

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public async Task GetMessageDomainAsync_should_return_domain_when_message_exists()
        {
            // Arrange
            var fixture = new Fixture();
            var messageId = fixture.Create<Guid>();

            var expectedDomain = "example.com";
            var document = new BsonDocument
            {
                { MessageDocumentProps.DomainPropName, expectedDomain }
            };

            var collectionMock = new Mock<IMongoCollection<BsonDocument>>();
            var asyncCursorMock = new Mock<IAsyncCursor<BsonDocument>>();

            // Configure the cursor to return the expected document
            asyncCursorMock
                .SetupSequence(x => x.MoveNextAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(true)
                .ReturnsAsync(false);

            asyncCursorMock
                .SetupGet(x => x.Current)
                .Returns(new List<BsonDocument> { document });

            collectionMock
                .Setup(x => x.FindAsync(It.IsAny<FilterDefinition<BsonDocument>>(), It.IsAny<FindOptions<BsonDocument, BsonDocument>>(), default))
                .ReturnsAsync(asyncCursorMock.Object);

            var sut = CreateSut(collectionMock.Object);

            // Act
            var result = await sut.GetMessageDomainAsync(messageId);

            // Assert
            Assert.Equal(expectedDomain, result);
        }

        [Fact]
        public async Task GetMessageDomainAsync_should_log_error_when_mongo_exception_is_thrown()
        {
            // Arrange
            var fixture = new Fixture();
            var messageId = fixture.Create<Guid>();

            var collectionMock = new Mock<IMongoCollection<BsonDocument>>();
            var loggerMock = new Mock<ILogger<MessageRepository>>();

            collectionMock
                .Setup(x => x.FindAsync(It.IsAny<FilterDefinition<BsonDocument>>(), It.IsAny<FindOptions<BsonDocument, BsonDocument>>(), default))
                .Throws(new MongoException("Test exception"));

            var sut = CreateSut(collectionMock.Object, loggerMock.Object);

            // Act & Assert
            await Assert.ThrowsAsync<MongoException>(() => sut.GetMessageDomainAsync(messageId));

            loggerMock.Verify(
                x => x.Log(
                    It.Is<LogLevel>(l => l == LogLevel.Error),
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString() == $"MongoException getting Message by {nameof(messageId)} {messageId}"),
                    It.IsAny<Exception>(),
                    It.Is<Func<It.IsAnyType, Exception, string>>((v, t) => true)),
                Times.Once);
        }

        [Fact]
        public async Task GetMessageDomainAsync_should_log_error_when_general_exception_is_thrown()
        {
            // Arrange
            var fixture = new Fixture();
            var messageId = fixture.Create<Guid>();

            var collectionMock = new Mock<IMongoCollection<BsonDocument>>();
            var loggerMock = new Mock<ILogger<MessageRepository>>();

            collectionMock
                .Setup(x => x.FindAsync(It.IsAny<FilterDefinition<BsonDocument>>(), It.IsAny<FindOptions<BsonDocument, BsonDocument>>(), default))
                .Throws(new Exception("Test exception"));

            var sut = CreateSut(collectionMock.Object, loggerMock.Object);

            // Act & Assert
            await Assert.ThrowsAsync<Exception>(() => sut.GetMessageDomainAsync(messageId));

            loggerMock.Verify(
                x => x.Log(
                    It.Is<LogLevel>(l => l == LogLevel.Error),
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString() == $"Unexpected error getting Message by {nameof(messageId)} {messageId}"),
                    It.IsAny<Exception>(),
                    It.Is<Func<It.IsAnyType, Exception, string>>((v, t) => true)),
                Times.Once);
        }

        [Fact]
        public async Task UpdateDeliveriesAsync_should_call_UpdateOneAsync_with_expected_filter_and_update()
        {
            // Arrange
            var fixture = new Fixture();
            var messageId = fixture.Create<Guid>();
            var sent = 2;
            var delivered = 1;
            var notDelivered = 3;

            var loggerMock = new Mock<ILogger<MessageRepository>>();
            var collectionMock = new Mock<IMongoCollection<BsonDocument>>();

            var sut = CreateSut(collectionMock.Object, loggerMock.Object);

            // Act
            await sut.UpdateDeliveriesAsync(messageId, sent, delivered, notDelivered);

            // Assert
            collectionMock.Verify(x => x.UpdateOneAsync(
                It.Is<FilterDefinition<BsonDocument>>(f => // the filter contains the messageId field
                    f.Render(BsonSerializer.SerializerRegistry.GetSerializer<BsonDocument>(), BsonSerializer.SerializerRegistry)
                    .ToString().Contains(messageId.ToString())),
                It.Is<UpdateDefinition<BsonDocument>>(u => // the udpdate definition contains sent, delivered and notDelivered fields
                    u.Render(BsonSerializer.SerializerRegistry.GetSerializer<BsonDocument>(), BsonSerializer.SerializerRegistry)
                    .ToString().Contains($"{MessageDocumentProps.SentPropName}") &&
                    u.Render(BsonSerializer.SerializerRegistry.GetSerializer<BsonDocument>(), BsonSerializer.SerializerRegistry)
                    .ToString().Contains($"{MessageDocumentProps.DeliveredPropName}") &&
                    u.Render(BsonSerializer.SerializerRegistry.GetSerializer<BsonDocument>(), BsonSerializer.SerializerRegistry)
                    .ToString().Contains($"{MessageDocumentProps.NotDeliveredPropName}")),
                It.IsAny<UpdateOptions>(), // options parameter
                It.IsAny<CancellationToken>()), // cancelation token parameter
                Times.Once);
        }

        [Fact]
        public async Task UpdateDeliveriesAsync_should_log_error_when_update_fails()
        {
            // Arrange
            var fixture = new Fixture();
            var messageId = fixture.Create<Guid>();
            var sent = 2;
            var delivered = 1;
            var notDelivered = 3;

            var loggerMock = new Mock<ILogger<MessageRepository>>();
            var collectionMock = new Mock<IMongoCollection<BsonDocument>>();

            collectionMock
                .Setup(x => x.UpdateOneAsync(
                    It.IsAny<FilterDefinition<BsonDocument>>(),
                    It.IsAny<UpdateDefinition<BsonDocument>>(),
                    It.IsAny<UpdateOptions>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new Exception("some error"));

            var sut = CreateSut(collectionMock.Object, loggerMock.Object);

            // Act
            await sut.UpdateDeliveriesAsync(messageId, sent, delivered, notDelivered);

            // Assert
            loggerMock.Verify(
                x => x.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) =>
                        v.ToString().Contains($"Error updating message counters with messageId {messageId}")),
                    It.IsAny<Exception>(),
                    It.Is<Func<It.IsAnyType, Exception, string>>((v, t) => true)),
                Times.Once);
        }

        [Fact]
        public async Task GetMessageSends_ShouldReturnZero_WhenResultIsNull()
        {
            // Arrange
            var domain = "test.com";
            var from = DateTimeOffset.UtcNow.AddDays(-1);
            var to = DateTimeOffset.UtcNow;

            var loggerMock = new Mock<ILogger<MessageRepository>>();
            var collectionMock = new Mock<IMongoCollection<BsonDocument>>();

            var mockCursor = new Mock<IAsyncCursor<BsonDocument>>();
            mockCursor.Setup(_ => _.Current).Returns(new List<BsonDocument>());
            mockCursor
                .SetupSequence(_ => _.MoveNext(It.IsAny<CancellationToken>()))
                .Returns(true)
                .Returns(false);
            mockCursor
                .SetupSequence(_ => _.MoveNextAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(true)
                .ReturnsAsync(false);

            collectionMock.Setup(c => c.AggregateAsync(
                It.IsAny<PipelineDefinition<BsonDocument, BsonDocument>>(),
                It.IsAny<AggregateOptions>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockCursor.Object);

            var sut = CreateSut(collectionMock.Object, loggerMock.Object);

            // Act
            var result = await sut.GetMessageSends(domain, from, to);

            // Assert
            Assert.Equal(0, result);
        }

        [Fact]
        public async Task GetMessageSends_ShouldReturnConsumedValue_WhenResultExists()
        {
            // Arrange
            var domain = "test.com";
            var from = DateTimeOffset.UtcNow.AddDays(-1);
            var to = DateTimeOffset.UtcNow;

            var expectedConsumed = 42;

            var documents = new List<BsonDocument>
            {
                new BsonDocument { { "Consumed", expectedConsumed } },
            };

            var loggerMock = new Mock<ILogger<MessageRepository>>();
            var collectionMock = new Mock<IMongoCollection<BsonDocument>>();

            var mockCursor = new Mock<IAsyncCursor<BsonDocument>>();
            mockCursor.Setup(_ => _.Current).Returns(documents);
            mockCursor
                .SetupSequence(_ => _.MoveNext(It.IsAny<CancellationToken>()))
                .Returns(true)
                .Returns(false);
            mockCursor
                .SetupSequence(_ => _.MoveNextAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(true)
                .ReturnsAsync(false);

            collectionMock.Setup(c => c.AggregateAsync(
                It.IsAny<PipelineDefinition<BsonDocument, BsonDocument>>(),
                It.IsAny<AggregateOptions>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockCursor.Object);

            var sut = CreateSut(collectionMock.Object, loggerMock.Object);

            // Act
            var result = await sut.GetMessageSends(domain, from, to);

            // Assert
            Assert.Equal(expectedConsumed, result);
        }

        [Fact]
        public async Task GetWebPushEventConsumed_ShouldThrowException_WhenAggregateFails()
        {
            // Arrange
            var domain = "test.com";
            var from = DateTimeOffset.UtcNow.AddDays(-1);
            var to = DateTimeOffset.UtcNow;

            var loggerMock = new Mock<ILogger<MessageRepository>>();
            var collectionMock = new Mock<IMongoCollection<BsonDocument>>();

            var expectedException = new Exception("Aggregate failed");

            collectionMock.Setup(c => c.AggregateAsync(
                It.IsAny<PipelineDefinition<BsonDocument, BsonDocument>>(),
                It.IsAny<AggregateOptions>(),
                It.IsAny<CancellationToken>()))
                .ThrowsAsync(expectedException);

            var sut = CreateSut(collectionMock.Object, loggerMock.Object);

            // Act & Assert
            var ex = await Assert.ThrowsAsync<Exception>(() =>
                sut.GetMessageSends(domain, from, to));

            Assert.Equal("Aggregate failed", ex.Message);
            loggerMock.Verify(
                x => x.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((o, t) => o.ToString().Contains("Error summarizing 'Messages' sends for domain:")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                Times.Once
            );
        }

        public static IEnumerable<object[]> GetEmptyWebPushEventsList()
        {
            yield return new object[] { null };
            yield return new object[] { new List<WebPushEvent>() };
        }

        [Theory]
        [MemberData(nameof(GetEmptyWebPushEventsList))]
        public async Task RegisterStatisticsAsync_ShouldReturnWithoutInvokeDB_WhenWebPushEventsIsEmpty(List<WebPushEvent> webPushEvents)
        {
            // Arrange
            Fixture fixture = new Fixture();
            var messageId = fixture.Create<Guid>();

            var collectionMock = new Mock<IMongoCollection<BsonDocument>>();

            var sut = CreateSut(collectionMock.Object);

            // Act
            await sut.RegisterStatisticsAsync(messageId, webPushEvents);

            // Assert
            collectionMock.Verify(c =>
                c.UpdateOneAsync(
                    It.IsAny<FilterDefinition<BsonDocument>>(),
                    It.IsAny<UpdateDefinition<BsonDocument>>(),
                    null,
                    It.IsAny<CancellationToken>()
                ),
                Times.Never);
        }

        [Fact]
        public async Task RegisterStatisticsAsync_ShouldReturnTrue_WhenInsertSucceeds()
        {
            // Arrange
            Fixture fixture = new Fixture();
            var messageId = fixture.Create<Guid>();

            var webPushEvent1 = new WebPushEvent
            {
                PushContactId = fixture.Create<string>(),
                MessageId = fixture.Create<Guid>(),
                Type = fixture.Create<int>(),
                Date = fixture.Create<DateTime>(),
            };

            var webPushEvents = new List<WebPushEvent>() { webPushEvent1 };

            var collectionMock = new Mock<IMongoCollection<BsonDocument>>();

            var sut = CreateSut(collectionMock.Object);

            // Act
            await sut.RegisterStatisticsAsync(messageId, webPushEvents);

            // Assert
            collectionMock.Verify(c =>
                c.UpdateOneAsync(
                    It.IsAny<FilterDefinition<BsonDocument>>(),
                    It.IsAny<UpdateDefinition<BsonDocument>>(),
                    null,
                    It.IsAny<CancellationToken>()
                ),
                Times.Once);
        }

        [Theory]
        [InlineData(WebPushEventType.Delivered, WebPushEventSubType.None,
            new[] { MessageDocumentProps.DeliveredPropName, MessageDocumentProps.SentPropName, MessageDocumentProps.BillableSendsPropName })]
        [InlineData(WebPushEventType.Received, WebPushEventSubType.None,
            new[] { MessageDocumentProps.ReceivedPropName })]
        [InlineData(WebPushEventType.Clicked, WebPushEventSubType.None,
            new[] { MessageDocumentProps.ClicksPropName })]
        [InlineData(WebPushEventType.ProcessingFailed, WebPushEventSubType.None,
            new[] { MessageDocumentProps.NotDeliveredPropName, MessageDocumentProps.SentPropName })]
        [InlineData(WebPushEventType.DeliveryFailedButRetry, WebPushEventSubType.None,
            new[] { MessageDocumentProps.NotDeliveredPropName, MessageDocumentProps.SentPropName })]
        [InlineData(WebPushEventType.DeliveryFailed, WebPushEventSubType.None,
            new[] { MessageDocumentProps.NotDeliveredPropName, MessageDocumentProps.SentPropName })]
        [InlineData(WebPushEventType.DeliveryFailed, WebPushEventSubType.InvalidSubcription,
            new[] { MessageDocumentProps.NotDeliveredPropName, MessageDocumentProps.SentPropName, MessageDocumentProps.BillableSendsPropName })]
        public async Task RegisterEventCount_ShouldUpdateExpectedFields(WebPushEventType type, WebPushEventSubType subtype, string[] expectedFieldsToUpdate)
        {
            // Arrange
            var fixture = new Fixture();
            var messageId = fixture.Create<Guid>();

            var collectionMock = new Mock<IMongoCollection<BsonDocument>>();

            var sut = CreateSut(collectionMock.Object);

            var webPushEvent = new WebPushEvent { Type = (int)type, SubType = (int)subtype };

            // Act
            await sut.RegisterEventCount(messageId, webPushEvent);

            // Assert
            collectionMock.Verify(x => x.UpdateOneAsync(
                It.Is<FilterDefinition<BsonDocument>>(f => // filter definition
                    f.Render(
                        BsonSerializer.SerializerRegistry.GetSerializer<BsonDocument>(),
                        BsonSerializer.SerializerRegistry
                    ).ToString().Contains(messageId.ToString())
                ),
                It.Is<UpdateDefinition<BsonDocument>>(u => // update definition
                    expectedFieldsToUpdate.All(field =>
                        u.Render(
                            BsonSerializer.SerializerRegistry.GetSerializer<BsonDocument>(),
                            BsonSerializer.SerializerRegistry
                        ).ToString().Contains(field))
                ),
                It.IsAny<UpdateOptions>(),  // options parameter
                It.IsAny<CancellationToken>()), // cancelation token parameter
                Times.Once);
        }

        [Fact]
        public async Task RegisterEventCount_ShouldNotUpdate_WhenTypeIsInvalid()
        {
            // Arrange
            var fixture = new Fixture();
            var messageId = fixture.Create<Guid>();

            var collectionMock = new Mock<IMongoCollection<BsonDocument>>();
            var loggerMock = new Mock<ILogger<MessageRepository>>();

            var sut = CreateSut(collectionMock.Object, loggerMock.Object);

            var invalidEvent = new WebPushEvent { Type = 999 }; // invalid type

            // Act
            await sut.RegisterEventCount(messageId, invalidEvent);

            // Assert
            collectionMock.Verify(c =>
                c.UpdateOneAsync(
                    It.IsAny<FilterDefinition<BsonDocument>>(),
                    It.IsAny<UpdateDefinition<BsonDocument>>(),
                    null,
                    It.IsAny<CancellationToken>()),
                Times.Never);

            loggerMock.Verify(
                x => x.Log(
                    LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Event type being registered is not valid for message with")),
                    null,
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                Times.Once);
        }

        [Fact]
        public async Task RegisterEventCount_ShouldLogError_WhenUpdateThrows()
        {
            // Arrange
            var fixture = new Fixture();
            var messageId = fixture.Create<Guid>();

            var collectionMock = new Mock<IMongoCollection<BsonDocument>>();
            var loggerMock = new Mock<ILogger<MessageRepository>>();

            collectionMock.Setup(c =>
                c.UpdateOneAsync(
                    It.IsAny<FilterDefinition<BsonDocument>>(),
                    It.IsAny<UpdateDefinition<BsonDocument>>(),
                    null,
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new Exception("Update failed on testing"));

            var sut = CreateSut(collectionMock.Object, loggerMock.Object);

            var evt = new WebPushEvent { Type = (int)WebPushEventType.Received };

            // Act
            await sut.RegisterEventCount(messageId, evt);

            // Assert
            loggerMock.Verify(
                x => x.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Error registering")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                Times.Once);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public async Task AddAsync_should_throw_ArgumentException_when_domain_is_null_or_empty(string invalidDomain)
        {
            // Arrange
            var fixture = new Fixture();
            var loggerMock = new Mock<ILogger<MessageRepository>>();
            var collectionMock = new Mock<IMongoCollection<BsonDocument>>();

            var sut = CreateSut(collectionMock.Object, loggerMock.Object);

            // Act & Assert
            var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
                sut.AddAsync(
                    fixture.Create<Guid>(),
                    invalidDomain,
                    "title",
                    "body",
                    "https://example.com",
                    0,
                    0,
                    0,
                    "https://image.com/image.png"
                ));

            Assert.Contains("domain", ex.Message);
        }

        [Theory]
        [InlineData(null, "body")]
        [InlineData("", "body")]
        [InlineData("title", null)]
        [InlineData("title", "")]
        public async Task AddAsync_should_throw_ArgumentException_when_title_or_body_is_null_or_empty(string title, string body)
        {
            // Arrange
            var fixture = new Fixture();
            var loggerMock = new Mock<ILogger<MessageRepository>>();
            var collectionMock = new Mock<IMongoCollection<BsonDocument>>();

            var sut = CreateSut(collectionMock.Object, loggerMock.Object);

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() =>
                sut.AddAsync(
                    fixture.Create<Guid>(),
                    "domain.com",
                    title,
                    body,
                    "https://example.com",
                    0,
                    0,
                    0,
                    "https://image.com/image.png"
                ));
        }

        [Fact]
        public async Task AddAsync_should_log_error_and_throw_when_insert_fails()
        {
            // Arrange
            var fixture = new Fixture();
            var messageId = fixture.Create<Guid>();

            var loggerMock = new Mock<ILogger<MessageRepository>>();
            var collectionMock = new Mock<IMongoCollection<BsonDocument>>();

            collectionMock
                .Setup(x => x.InsertOneAsync(
                    It.IsAny<BsonDocument>(),
                    It.IsAny<InsertOneOptions>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new Exception("insert error"));

            var sut = CreateSut(collectionMock.Object, loggerMock.Object);

            // Act & Assert
            var ex = await Assert.ThrowsAsync<Exception>(() =>
                sut.AddAsync(
                    messageId,
                    "domain.com",
                    "title",
                    "body",
                    "https://example.com",
                    0,
                    0,
                    0,
                    "https://image.com/image.png"
                ));

            Assert.Equal("insert error", ex.Message);

            loggerMock.Verify(
                x => x.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) =>
                        v.ToString().Contains($"Error inserting message with messageId {messageId}")),
                    It.IsAny<Exception>(),
                    It.Is<Func<It.IsAnyType, Exception, string>>((v, t) => true)),
                Times.Once);
        }

        [Fact]
        public async Task AddAsync_should_insert_document_without_actions()
        {
            // Arrange
            var fixture = new Fixture();
            var messageId = fixture.Create<Guid>();

            var loggerMock = new Mock<ILogger<MessageRepository>>();
            var collectionMock = new Mock<IMongoCollection<BsonDocument>>();

            var sut = CreateSut(collectionMock.Object, loggerMock.Object);

            // Act
            await sut.AddAsync(
                messageId,
                "domain.com",
                "title",
                "body",
                "https://example.com",
                1,
                2,
                3,
                "https://image.com/img.png"
            );

            // Assert
            collectionMock.Verify(x => x.InsertOneAsync(
                It.Is<BsonDocument>(d =>
                    d.Contains(MessageDocumentProps.TitlePropName) &&
                    d[MessageDocumentProps.TitlePropName] == "title" &&
                    !d.Contains(MessageDocumentProps.ActionsPropName)), // it has not 'actions' field
                It.IsAny<InsertOneOptions>(),
                It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task AddAsync_should_insert_document_with_actions()
        {
            // Arrange
            var fixture = new Fixture();
            var messageId = fixture.Create<Guid>();

            var loggerMock = new Mock<ILogger<MessageRepository>>();
            var collectionMock = new Mock<IMongoCollection<BsonDocument>>();

            var sut = CreateSut(collectionMock.Object, loggerMock.Object);

            var actions = new List<MessageActionDTO>
            {
                new MessageActionDTO
                {
                    Action = "accept",
                    Title = "Aceptar",
                    Icon = "https://icon.png",
                    Link = "https://link.com"
                }
            };

            // Act
            await sut.AddAsync(
                messageId,
                "domain.com",
                "title",
                "body",
                "https://example.com",
                1,
                2,
                3,
                "https://image.com/img.png",
                actions
            );

            // Assert
            collectionMock.Verify(x => x.InsertOneAsync(
                It.Is<BsonDocument>(d =>
                    d.Contains(MessageDocumentProps.ActionsPropName) &&
                    d[MessageDocumentProps.ActionsPropName].AsBsonArray.Count == 1 &&
                    d[MessageDocumentProps.ActionsPropName][0][MessageDocumentProps.Actions_ActionPropName] == "accept" &&
                    d[MessageDocumentProps.ActionsPropName][0][MessageDocumentProps.Actions_TitlePropName] == "Aceptar"),
                It.IsAny<InsertOneOptions>(),
                It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task GetMessageDetailsByMessageIdAsync_should_return_null_when_message_not_found()
        {
            // Arrange
            var fixture = new Fixture();
            var messageId = fixture.Create<Guid>();

            var loggerMock = new Mock<ILogger<MessageRepository>>();
            var collectionMock = new Mock<IMongoCollection<BsonDocument>>();
            var cursorMock = new Mock<IAsyncCursor<BsonDocument>>();

            cursorMock.SetupSequence(c => c.MoveNextAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(false); // no documents found

            collectionMock
                .Setup(x => x.FindAsync(
                    It.IsAny<FilterDefinition<BsonDocument>>(),
                    It.IsAny<FindOptions<BsonDocument, BsonDocument>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(cursorMock.Object);

            var sut = CreateSut(collectionMock.Object, loggerMock.Object);

            // Act
            var result = await sut.GetMessageDetailsByMessageIdAsync(messageId);

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public async Task GetMessageDetailsByMessageIdAsync_should_return_message_with_empty_actions()
        {
            // Arrange
            var fixture = new Fixture();
            var messageId = fixture.Create<Guid>();

            // no "actions"
            var bsonDoc = new BsonDocument
            {
                { MessageDocumentProps.MessageIdPropName, new BsonBinaryData(messageId, GuidRepresentation.Standard) },
                { MessageDocumentProps.DomainPropName, "test.com" },
                { MessageDocumentProps.TitlePropName, "Title" },
                { MessageDocumentProps.BodyPropName, "Body" },
                { MessageDocumentProps.OnClickLinkPropName, "https://click.com" },
                { MessageDocumentProps.SentPropName, 10 },
                { MessageDocumentProps.DeliveredPropName, 5 },
                { MessageDocumentProps.NotDeliveredPropName, 2 },
                { MessageDocumentProps.ImageUrlPropName, "https://image.com" }
            };

            var loggerMock = new Mock<ILogger<MessageRepository>>();
            var collectionMock = new Mock<IMongoCollection<BsonDocument>>();
            var cursorMock = new Mock<IAsyncCursor<BsonDocument>>();

            cursorMock.SetupSequence(c => c.MoveNextAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(true)
                .ReturnsAsync(false);
            cursorMock.SetupGet(c => c.Current).Returns(new[] { bsonDoc });

            collectionMock
                .Setup(x => x.FindAsync(
                    It.IsAny<FilterDefinition<BsonDocument>>(),
                    It.IsAny<FindOptions<BsonDocument, BsonDocument>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(cursorMock.Object);

            var sut = CreateSut(collectionMock.Object, loggerMock.Object);

            // Act
            var result = await sut.GetMessageDetailsByMessageIdAsync(messageId);

            // Assert
            Assert.NotNull(result);
            Assert.Empty(result.Actions);
        }

        [Fact]
        public async Task GetMessageDetailsByMessageIdAsync_should_return_message_with_actions()
        {
            // Arrange
            var fixture = new Fixture();
            var messageId = fixture.Create<Guid>();

            var bsonDoc = new BsonDocument
            {
                { MessageDocumentProps.MessageIdPropName, new BsonBinaryData(messageId, GuidRepresentation.Standard) },
                { MessageDocumentProps.DomainPropName, "test.com" },
                { MessageDocumentProps.TitlePropName, "Title" },
                { MessageDocumentProps.BodyPropName, "Body" },
                { MessageDocumentProps.OnClickLinkPropName, "https://click.com" },
                { MessageDocumentProps.SentPropName, 10 },
                { MessageDocumentProps.DeliveredPropName, 5 },
                { MessageDocumentProps.NotDeliveredPropName, 2 },
                { MessageDocumentProps.ImageUrlPropName, "https://image.com" },
                { MessageDocumentProps.ActionsPropName, new BsonArray
                    {
                        new BsonDocument
                        {
                            { MessageDocumentProps.Actions_ActionPropName, "Action1" },
                            { MessageDocumentProps.Actions_TitlePropName, "Title1" },
                            { MessageDocumentProps.Actions_IconPropName, "https://icon.png" },
                            { MessageDocumentProps.Actions_LinkPropName, "https://link.com" }
                        }
                    }
                }
            };

            var loggerMock = new Mock<ILogger<MessageRepository>>();
            var collectionMock = new Mock<IMongoCollection<BsonDocument>>();
            var cursorMock = new Mock<IAsyncCursor<BsonDocument>>();

            cursorMock.SetupSequence(c => c.MoveNextAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(true)
                .ReturnsAsync(false);
            cursorMock.SetupGet(c => c.Current).Returns(new[] { bsonDoc });

            collectionMock
                .Setup(x => x.FindAsync(
                    It.IsAny<FilterDefinition<BsonDocument>>(),
                    It.IsAny<FindOptions<BsonDocument, BsonDocument>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(cursorMock.Object);

            var sut = CreateSut(collectionMock.Object, loggerMock.Object);

            // Act
            var result = await sut.GetMessageDetailsByMessageIdAsync(messageId);

            // Assert
            Assert.NotNull(result);
            Assert.Single(result.Actions);
            Assert.True(result.Actions[0].Action == "Action1");
            Assert.True(result.Actions[0].Title == "Title1");
        }

        [Fact]
        public async Task GetMessageDetailsByMessageIdAsync_should_log_error_and_throw_when_exception_occurs()
        {
            // Arrange
            var fixture = new Fixture();
            var messageId = fixture.Create<Guid>();

            var loggerMock = new Mock<ILogger<MessageRepository>>();
            var collectionMock = new Mock<IMongoCollection<BsonDocument>>();

            var exceptionMessage = "db error";

            collectionMock
                .Setup(x => x.FindAsync(
                    It.IsAny<FilterDefinition<BsonDocument>>(),
                    It.IsAny<FindOptions<BsonDocument, BsonDocument>>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new Exception(exceptionMessage));

            var sut = CreateSut(collectionMock.Object, loggerMock.Object);

            // Act & Assert
            var exception = await Assert.ThrowsAsync<Exception>(() => sut.GetMessageDetailsByMessageIdAsync(messageId));
            Assert.Equal(exceptionMessage, exception.Message);

            loggerMock.Verify(
                x => x.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, _) =>
                        v.ToString().Contains($"Error getting message with messageId {messageId}")),
                    It.IsAny<Exception>(),
                    It.Is<Func<It.IsAnyType, Exception, string>>((v, t) => true)),
                Times.Once);
        }
    }
}
