using AutoFixture;
using Doppler.PushContact.Models.Entities;
using Doppler.PushContact.Models.Enums;
using Doppler.PushContact.Repositories;
using Doppler.PushContact.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Doppler.PushContact.Test.Repositories
{
    public class WebPushEventRepositoryTest
    {
        private readonly Mock<IMongoClient> _mockMongoClient;
        private readonly Mock<IMongoDatabase> _mockDatabase;
        private readonly Mock<IMongoCollection<BsonDocument>> _mockCollection;
        private readonly Mock<IOptions<PushMongoContextSettings>> _mockSettings;
        private readonly Mock<ILogger<WebPushEventRepository>> _mockLogger;
        private readonly WebPushEventRepository _repository;

        public WebPushEventRepositoryTest()
        {
            _mockMongoClient = new Mock<IMongoClient>();
            _mockDatabase = new Mock<IMongoDatabase>();
            _mockCollection = new Mock<IMongoCollection<BsonDocument>>();
            _mockSettings = new Mock<IOptions<PushMongoContextSettings>>();
            _mockLogger = new Mock<ILogger<WebPushEventRepository>>();

            _mockSettings.Setup(s => s.Value).Returns(new PushMongoContextSettings
            {
                DatabaseName = "testdb",
                WebPushEventCollectionName = "webPushEvents"
            });

            _mockMongoClient.Setup(c => c.GetDatabase(It.IsAny<string>(), null))
                .Returns(_mockDatabase.Object);

            _mockDatabase.Setup(d => d.GetCollection<BsonDocument>(It.IsAny<string>(), null))
                .Returns(_mockCollection.Object);

            _repository = new WebPushEventRepository(_mockMongoClient.Object, _mockSettings.Object, _mockLogger.Object);
        }

        [Fact]
        public async Task GetWebPushEventSummarization_ShouldReturnSummarizedData_WhenDataExists()
        {
            // Arrange
            var messageId = Guid.NewGuid();
            var formattedMessageId = new BsonBinaryData(messageId, GuidRepresentation.Standard);

            var documents = new List<BsonDocument>
            {
                new BsonDocument
                {
                    { "_id", formattedMessageId },
                    { "Delivered", 1 },
                    { "NotDelivered", 1 }
                }
            };

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

            _mockCollection.Setup(c => c.AggregateAsync(
                It.IsAny<PipelineDefinition<BsonDocument, BsonDocument>>(),
                It.IsAny<AggregateOptions>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockCursor.Object);

            // Act
            var result = await _repository.GetWebPushEventSummarization(messageId);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(messageId, result.MessageId);
            Assert.Equal(2, result.SentQuantity);
            Assert.Equal(1, result.Delivered);
            Assert.Equal(1, result.NotDelivered);
        }

        [Fact]
        public async Task GetWebPushEventSummarization_ShouldReturnEmptyStats_WhenNoDataExists()
        {
            // Arrange
            var messageId = Guid.NewGuid();

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

            _mockCollection.Setup(c => c.AggregateAsync(
                It.IsAny<PipelineDefinition<BsonDocument, BsonDocument>>(),
                It.IsAny<AggregateOptions>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockCursor.Object);

            // Act
            var result = await _repository.GetWebPushEventSummarization(messageId);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(messageId, result.MessageId);
            Assert.Equal(0, result.SentQuantity);
            Assert.Equal(0, result.Delivered);
            Assert.Equal(0, result.NotDelivered);
        }

        [Fact]
        public async Task InsertAsync_ShouldReturnTrue_WhenInsertSucceeds()
        {
            var fixture = new Fixture();

            // Arrange
            var webPushEvent = new WebPushEvent
            {
                PushContactId = fixture.Create<string>(),
                MessageId = fixture.Create<Guid>(),
                Type = fixture.Create<int>(),
                Date = fixture.Create<DateTime>(),
            };

            _mockCollection.Setup(c => c.InsertOneAsync(
                It.IsAny<BsonDocument>(),
                null,
                It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            // Act
            var result = await _repository.InsertAsync(webPushEvent, CancellationToken.None);

            // Assert
            Assert.True(result);
            _mockCollection.Verify(c => c.InsertOneAsync(
                It.Is<BsonDocument>(
                    doc => doc.Contains(WebPushEventDocumentProps.PushContactId_PropName) &&
                    doc.Contains(WebPushEventDocumentProps.MessageId_PropName) &&
                    doc.Contains(WebPushEventDocumentProps.Type_PropName) &&
                    doc.Contains(WebPushEventDocumentProps.Date_PropName)
                ),
                null,
                It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task IsWebPushEventRegistered_ShouldReturnTrue_WhenEventExists()
        {
            // Arrange
            var pushContactId = "testPushContactId";
            var messageId = Guid.NewGuid();
            var eventType = WebPushEventType.Delivered;

            var formattedMessageId = new BsonBinaryData(messageId, GuidRepresentation.Standard);

            var document = new BsonDocument
            {
                { WebPushEventDocumentProps.PushContactId_PropName, pushContactId },
                { WebPushEventDocumentProps.Type_PropName, (int)eventType },
                { WebPushEventDocumentProps.MessageId_PropName, formattedMessageId }
            };

            var mockCursor = new Mock<IAsyncCursor<BsonDocument>>();
            mockCursor.SetupSequence(_ => _.MoveNext(It.IsAny<CancellationToken>()))
                .Returns(true)
                .Returns(false);
            mockCursor.SetupSequence(_ => _.MoveNextAsync(It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult(true))
                .Returns(Task.FromResult(false));
            mockCursor.Setup(_ => _.Current).Returns(new List<BsonDocument> { document });

            _mockCollection
                .Setup(c => c.FindAsync(
                        It.IsAny<FilterDefinition<BsonDocument>>(),
                        It.IsAny<FindOptions<BsonDocument>>(),
                        It.IsAny<CancellationToken>()
                    )
                )
                .ReturnsAsync(mockCursor.Object);

            // Act
            var result = await _repository.IsWebPushEventRegistered(pushContactId, messageId, eventType);

            // Assert
            Assert.True(result);
        }

        [Fact]
        public async Task IsWebPushEventRegistered_ShouldReturnFalse_WhenEventDoesNotExist()
        {
            // Arrange
            var pushContactId = "testPushContactId";
            var messageId = Guid.NewGuid();
            var eventType = WebPushEventType.Delivered;

            var mockCursor = new Mock<IAsyncCursor<BsonDocument>>();
            mockCursor.SetupSequence(_ => _.MoveNext(It.IsAny<CancellationToken>()))
                .Returns(true)
                .Returns(false);
            mockCursor.SetupSequence(_ => _.MoveNextAsync(It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult(true))
                .Returns(Task.FromResult(false));
            mockCursor.Setup(_ => _.Current).Returns(new List<BsonDocument>());

            _mockCollection
                .Setup(c => c.FindAsync(
                        It.IsAny<FilterDefinition<BsonDocument>>(),
                        It.IsAny<FindOptions<BsonDocument>>(),
                        It.IsAny<CancellationToken>()
                    )
                )
                .ReturnsAsync(mockCursor.Object);

            // Act
            var result = await _repository.IsWebPushEventRegistered(pushContactId, messageId, eventType);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public async Task IsWebPushEventRegistered_ShouldLogError_WhenMongoDBThrowsException()
        {
            // Arrange
            var pushContactId = "testPushContactId";
            var messageId = Guid.NewGuid();
            var eventType = WebPushEventType.Delivered;

            _mockCollection
                .Setup(c => c.FindAsync(
                    It.IsAny<FilterDefinition<BsonDocument>>(),
                    It.IsAny<FindOptions<BsonDocument>>(),
                    It.IsAny<CancellationToken>()
                ))
                .Throws(new Exception("MongoDB exception"));

            // Act & Assert
            var exception = await Assert.ThrowsAsync<Exception>(() =>
                _repository.IsWebPushEventRegistered(pushContactId, messageId, eventType)
            );

            Assert.Equal("MongoDB exception", exception.Message);

            _mockLogger.Verify(
                logger => logger.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Error checking if WebPushEvent exists for pushContactId:")),
                    exception,
                    It.Is<Func<It.IsAnyType, Exception, string>>((v, t) => true)),
                Times.Once);
        }

        [Fact]
        public async Task GetWebPushEventConsumed_ShouldReturnZero_WhenResultIsNull()
        {
            // Arrange
            var domain = "test.com";
            var from = DateTimeOffset.UtcNow.AddDays(-1);
            var to = DateTimeOffset.UtcNow;

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

            _mockCollection.Setup(c => c.AggregateAsync(
                It.IsAny<PipelineDefinition<BsonDocument, BsonDocument>>(),
                It.IsAny<AggregateOptions>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockCursor.Object);

            // Act
            var result = await _repository.GetWebPushEventConsumed(domain, from, to);

            // Assert
            Assert.Equal(0, result);
        }

        [Fact]
        public async Task GetWebPushEventConsumed_ShouldReturnConsumedValue_WhenResultExists()
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

            _mockCollection.Setup(c => c.AggregateAsync(
                It.IsAny<PipelineDefinition<BsonDocument, BsonDocument>>(),
                It.IsAny<AggregateOptions>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockCursor.Object);

            // Act
            var result = await _repository.GetWebPushEventConsumed(domain, from, to);

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

            var expectedException = new Exception("Aggregate failed");

            _mockCollection.Setup(c => c.AggregateAsync(
                It.IsAny<PipelineDefinition<BsonDocument, BsonDocument>>(),
                It.IsAny<AggregateOptions>(),
                It.IsAny<CancellationToken>()))
                .ThrowsAsync(expectedException);

            // Act & Assert
            var ex = await Assert.ThrowsAsync<Exception>(() =>
                _repository.GetWebPushEventConsumed(domain, from, to));

            Assert.Equal("Aggregate failed", ex.Message);
            _mockLogger.Verify(
                x => x.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((o, t) => o.ToString().Contains("Error summarizing billable 'WebPushEvents' for domain:")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                Times.Once
            );
        }
    }
}
