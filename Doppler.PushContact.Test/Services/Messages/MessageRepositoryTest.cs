using Doppler.PushContact.Services.Messages;
using Doppler.PushContact.Services;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Moq;
using Microsoft.Extensions.Logging;
using AutoFixture;
using MongoDB.Bson;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using System;
using Xunit;
using MongoDB.Bson.Serialization;

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
    }
}
