using AutoFixture;
using Doppler.PushContact.Models.Entities;
using Doppler.PushContact.Repositories;
using Doppler.PushContact.Services;
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

namespace Doppler.PushContact.Test.Repositories
{
    public class MessageStatsRepositoryTest
    {
        private readonly Mock<IMongoClient> _mockMongoClient;
        private readonly Mock<IMongoDatabase> _mockDatabase;
        private readonly Mock<IMongoCollection<MessageStats>> _mockCollection;
        private readonly Mock<IOptions<PushMongoContextSettings>> _mockSettings;
        private readonly MessageStatsRepository _repository;

        public MessageStatsRepositoryTest()
        {
            _mockMongoClient = new Mock<IMongoClient>();
            _mockDatabase = new Mock<IMongoDatabase>();
            _mockCollection = new Mock<IMongoCollection<MessageStats>>();
            _mockSettings = new Mock<IOptions<PushMongoContextSettings>>();

            _mockSettings.Setup(s => s.Value).Returns(new PushMongoContextSettings
            {
                DatabaseName = "testdb",
                MessageStatsCollectionName = "messageStats"
            });

            _mockMongoClient.Setup(c => c.GetDatabase(It.IsAny<string>(), null))
                .Returns(_mockDatabase.Object);

            _mockDatabase.Setup(d => d.GetCollection<MessageStats>(It.IsAny<string>(), null))
                .Returns(_mockCollection.Object);

            _repository = new MessageStatsRepository(_mockMongoClient.Object, _mockSettings.Object);
        }

        [Fact]
        public async Task BulkUpsertStatsAsync_ShouldCallBulkWriteOnce()
        {
            // Arrange
            var fixture = new Fixture();
            var domain = fixture.Create<string>();
            var messageId = fixture.Create<Guid>();

            var stats = new List<MessageStats>
            {
                new MessageStats
                {
                    Domain = domain,
                    MessageId = messageId,
                    Date = DateTime.UtcNow,
                    Sent = 5,
                    Delivered = 3,
                    NotDelivered = 1,
                    Click = 2
                }
            };

            _mockCollection
                .Setup(c => c.BulkWriteAsync(
                    It.IsAny<IEnumerable<WriteModel<MessageStats>>>(),
                    null,
                    default))
                .ReturnsAsync((BulkWriteResult<MessageStats>)null!);

            // Act
            await _repository.BulkUpsertStatsAsync(stats);

            // Assert
            _mockCollection.Verify(c => c.BulkWriteAsync(
                It.IsAny<IEnumerable<WriteModel<MessageStats>>>(),
                null,
                default), Times.Once);
        }

        [Fact]
        public async Task BulkUpsertStatsAsync_ShouldGenerateOneUpdatePerItem()
        {
            // Arrange
            var stats = new List<MessageStats>
            {
                new MessageStats { Domain = "a", MessageId = Guid.NewGuid(), Date = DateTime.UtcNow },
                new MessageStats { Domain = "b", MessageId = Guid.NewGuid(), Date = DateTime.UtcNow }
            };

            IEnumerable<WriteModel<MessageStats>> capturedUpdateModels = null!;

            _mockCollection
                .Setup(c => c.BulkWriteAsync(It.IsAny<IEnumerable<WriteModel<MessageStats>>>(), null, default))
                .Callback<IEnumerable<WriteModel<MessageStats>>, BulkWriteOptions, CancellationToken>((models, _, _) =>
                {
                    capturedUpdateModels = models;
                })
                .ReturnsAsync((BulkWriteResult<MessageStats>)null!);

            // Act
            await _repository.BulkUpsertStatsAsync(stats);

            // Assert
            Assert.Equal(2, capturedUpdateModels.Count());
        }

        [Fact]
        public async Task BulkUpsertStatsAsync_ShouldSetUpsertFlagTrue()
        {
            // Arrange
            var stat = new MessageStats
            {
                Domain = "x",
                MessageId = Guid.NewGuid(),
                Date = DateTime.UtcNow
            };

            IEnumerable<WriteModel<MessageStats>> capturedUpdateModels = null!;

            _mockCollection
                .Setup(c => c.BulkWriteAsync(It.IsAny<IEnumerable<WriteModel<MessageStats>>>(), null, default))
                .Callback<IEnumerable<WriteModel<MessageStats>>, BulkWriteOptions, CancellationToken>((models, _, _) =>
                {
                    capturedUpdateModels = models;
                })
                .ReturnsAsync((BulkWriteResult<MessageStats>)null!);

            // Act
            await _repository.BulkUpsertStatsAsync(new[] { stat });

            // Assert
            var update = capturedUpdateModels.First() as UpdateOneModel<MessageStats>;
            Assert.True(update.IsUpsert);
        }

        [Fact]
        public async Task BulkUpsertStatsAsync_ShouldFilterByDomainMessageIdAndDate()
        {
            // Arrange
            var stat = new MessageStats
            {
                Domain = "mysite.com",
                MessageId = Guid.NewGuid(),
                Date = DateTime.UtcNow
            };

            IEnumerable<WriteModel<MessageStats>> capturedModels = null!;

            _mockCollection
                .Setup(c => c.BulkWriteAsync(It.IsAny<IEnumerable<WriteModel<MessageStats>>>(), null, default))
                .Callback<IEnumerable<WriteModel<MessageStats>>, BulkWriteOptions, CancellationToken>((models, _, _) =>
                {
                    capturedModels = models;
                })
                .ReturnsAsync((BulkWriteResult<MessageStats>)null!);

            // Act
            await _repository.BulkUpsertStatsAsync(new[] { stat });

            // Assert
            var update = (UpdateOneModel<MessageStats>)capturedModels.First();

            var renderedFilter = update.Filter.Render(
                BsonSerializer.SerializerRegistry.GetSerializer<MessageStats>(),
                BsonSerializer.SerializerRegistry
            );

            Assert.Contains("mysite.com", renderedFilter.ToString());
            Assert.Contains(stat.MessageId.ToString(), renderedFilter.ToString());
            Assert.Contains(stat.Date.ToUniversalTime().ToString("s"), renderedFilter.ToString());
        }

        [Fact]
        public async Task BulkUpsertStatsAsync_ShouldIncrementCounters()
        {
            // Arrange
            var stat = new MessageStats
            {
                Domain = "test",
                MessageId = Guid.NewGuid(),
                Date = DateTime.UtcNow,
                Sent = 10,
                Delivered = 5,
                NotDelivered = 3,
                Click = 1,
                Received = 2,
                BillableSends = 9,
                ActionClick = 4
            };

            IEnumerable<WriteModel<MessageStats>> capturedModels = null!;

            _mockCollection
                .Setup(c => c.BulkWriteAsync(It.IsAny<IEnumerable<WriteModel<MessageStats>>>(), null, default))
                .Callback<IEnumerable<WriteModel<MessageStats>>, BulkWriteOptions, CancellationToken>((models, _, _) =>
                {
                    capturedModels = models;
                })
                .ReturnsAsync((BulkWriteResult<MessageStats>)null!);

            // Act
            await _repository.BulkUpsertStatsAsync(new[] { stat });

            // Assert
            var update = (UpdateOneModel<MessageStats>)capturedModels.First();

            var renderedUpdate = update.Update.Render(
                BsonSerializer.SerializerRegistry.GetSerializer<MessageStats>(),
                BsonSerializer.SerializerRegistry
            );

            Assert.Contains($"\"{MessageStatsDocumentProps.Sent_PropName}\" : 10", renderedUpdate.ToString());
            Assert.Contains($"\"{MessageStatsDocumentProps.Delivered_PropName}\" : 5", renderedUpdate.ToString());
            Assert.Contains($"\"{MessageStatsDocumentProps.NotDelivered_PropName}\" : 3", renderedUpdate.ToString());
            Assert.Contains($"\"{MessageStatsDocumentProps.Click_PropName}\" : 1", renderedUpdate.ToString());
            Assert.Contains($"\"{MessageStatsDocumentProps.Received_PropName}\" : 2", renderedUpdate.ToString());
            Assert.Contains($"\"{MessageStatsDocumentProps.BillableSends_PropName}\" : 9", renderedUpdate.ToString());
            Assert.Contains($"\"{MessageStatsDocumentProps.ActionClick_PropName}\" : 4", renderedUpdate.ToString());
        }

        [Fact]
        public async Task UpsertMessageStatsAsync_ShouldThrowArgumentNullException_WhenMessageStatsIsNull()
        {
            // Act & Assert
            await Assert.ThrowsAsync<ArgumentNullException>(() => _repository.UpsertMessageStatsAsync(null!));
        }

        [Fact]
        public async Task UpsertMessageStatsAsync_ShouldCallUpdateOneAsync_WithUpsertTrue()
        {
            // Arrange
            var stat = new MessageStats
            {
                Domain = "example.com",
                MessageId = Guid.NewGuid(),
                Date = DateTime.UtcNow,
                Sent = 1,
                Delivered = 2,
                NotDelivered = 3,
                Click = 4,
                Received = 5,
                BillableSends = 6,
                ActionClick = 7
            };

            // Capturar los valores que recibe la llamada al mock
            FilterDefinition<MessageStats> capturedFilter = null!;
            UpdateDefinition<MessageStats> capturedUpdate = null!;
            UpdateOptions capturedOptions = null!;

            _mockCollection
                .Setup(c => c.UpdateOneAsync(
                    It.IsAny<FilterDefinition<MessageStats>>(),
                    It.IsAny<UpdateDefinition<MessageStats>>(),
                    It.IsAny<UpdateOptions>(),
                    default))
                .Callback<FilterDefinition<MessageStats>, UpdateDefinition<MessageStats>, UpdateOptions, CancellationToken>(
                    (filter, update, options, _) =>
                    {
                        capturedFilter = filter;
                        capturedUpdate = update;
                        capturedOptions = options;
                    })
                .ReturnsAsync((UpdateResult)null!);

            // Act
            await _repository.UpsertMessageStatsAsync(stat);

            // Assert
            _mockCollection.Verify(c => c.UpdateOneAsync(
                It.IsAny<FilterDefinition<MessageStats>>(),
                It.IsAny<UpdateDefinition<MessageStats>>(),
                It.IsAny<UpdateOptions>(),
                default), Times.Once);

            Assert.True(capturedOptions.IsUpsert);

            // Verificamos que el filtro incluya los 3 campos esperados
            var renderedFilter = capturedFilter.Render(
                BsonSerializer.SerializerRegistry.GetSerializer<MessageStats>(),
                BsonSerializer.SerializerRegistry);

            Assert.Contains("example.com", renderedFilter.ToString());
            Assert.Contains(stat.MessageId.ToString(), renderedFilter.ToString());
            Assert.Contains(stat.Date.ToUniversalTime().ToString("s"), renderedFilter.ToString());

            // Verificamos que haya increments en los campos
            var renderedUpdate = capturedUpdate.Render(
                BsonSerializer.SerializerRegistry.GetSerializer<MessageStats>(),
                BsonSerializer.SerializerRegistry);

            Assert.Contains($"\"{MessageStatsDocumentProps.Sent_PropName}\" : 1", renderedUpdate.ToString());
            Assert.Contains($"\"{MessageStatsDocumentProps.Delivered_PropName}\" : 2", renderedUpdate.ToString());
            Assert.Contains($"\"{MessageStatsDocumentProps.NotDelivered_PropName}\" : 3", renderedUpdate.ToString());
            Assert.Contains($"\"{MessageStatsDocumentProps.Click_PropName}\" : 4", renderedUpdate.ToString());
            Assert.Contains($"\"{MessageStatsDocumentProps.Received_PropName}\" : 5", renderedUpdate.ToString());
            Assert.Contains($"\"{MessageStatsDocumentProps.BillableSends_PropName}\" : 6", renderedUpdate.ToString());
            Assert.Contains($"\"{MessageStatsDocumentProps.ActionClick_PropName}\" : 7", renderedUpdate.ToString());
        }

        [Fact]
        public async Task GetMessageStatsAsync_ShouldThrowException_WhenMongoThrows()
        {
            // Arrange
            var fixture = new Fixture();
            var domain = fixture.Create<string>();
            var messageId = fixture.Create<Guid>();
            var dateFrom = DateTimeOffset.UtcNow.AddDays(-1);
            var dateTo = DateTimeOffset.UtcNow;

            var mockAsyncCursor = new Mock<IAsyncCursor<BsonDocument>>();

            _mockCollection
                .Setup(c => c.AggregateAsync(It.IsAny<PipelineDefinition<MessageStats, BsonDocument>>(),
                    It.IsAny<AggregateOptions>(), default))
                .Throws(new MongoException("Simulated failure"));

            // Act & Assert
            await Assert.ThrowsAsync<MongoException>(() =>
                _repository.GetMessageStatsAsync(domain, messageId, dateFrom, dateTo));
        }

        [Theory]
        [InlineData(null, null)] // sin domain ni messageId
        [InlineData("example.com", null)] // con domain sin messageId
        [InlineData(null, "00000000-0000-0000-0000-000000000001")] // sin domain con messageId
        [InlineData("example.com", "00000000-0000-0000-0000-000000000002")] // ambos presentes
        public async Task GetMessageStatsAsync_ShouldReturnAggregatedStats_WhenSuccess(string domain, string messageIdString)
        {
            // Arrange
            var messageId = string.IsNullOrEmpty(messageIdString)
                ? (Guid?)null
                : Guid.Parse(messageIdString);

            var dateFrom = DateTimeOffset.UtcNow.AddDays(-1);
            var dateTo = DateTimeOffset.UtcNow;

            var aggregateResult = new BsonDocument
            {
                { "Sent", 10 },
                { "Delivered", 8 },
                { "NotDelivered", 2 },
                { "Received", 7 },
                { "Click", 3 },
                { "ActionClick", 1 },
                { "BillableSends", 9 }
            };

            var mockCursor = new Mock<IAsyncCursor<BsonDocument>>();
            mockCursor.SetupSequence(c => c.MoveNext(It.IsAny<CancellationToken>()))
                .Returns(true)
                .Returns(false);
            mockCursor.SetupSequence(_ => _.MoveNextAsync(It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult(true))
                .Returns(Task.FromResult(false));
            mockCursor.Setup(_ => _.Current).Returns(new[] { aggregateResult });

            _mockCollection
                .Setup(c => c.AggregateAsync(It.IsAny<PipelineDefinition<MessageStats, BsonDocument>>(),
                    It.IsAny<AggregateOptions>(), default))
                .ReturnsAsync(mockCursor.Object);

            // Act
            var result = await _repository.GetMessageStatsAsync(domain, messageId, dateFrom, dateTo);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(domain, result.Domain);
            Assert.Equal(messageId ?? Guid.Empty, result.MessageId);
            Assert.Equal(dateFrom, result.DateFrom);
            Assert.Equal(dateTo, result.DateTo);
            Assert.Equal(10, result.Sent);
            Assert.Equal(8, result.Delivered);
            Assert.Equal(2, result.NotDelivered);
            Assert.Equal(7, result.Received);
            Assert.Equal(3, result.Click);
            Assert.Equal(1, result.ActionClick);
            Assert.Equal(9, result.BillableSends);

            _mockCollection.Verify(c => c.AggregateAsync(
                It.IsAny<PipelineDefinition<MessageStats, BsonDocument>>(),
                It.IsAny<AggregateOptions>(),
                default), Times.Once);
        }

        [Fact]
        public async Task GetMessageStatsAsync_ShouldReturnZeros_WhenNoResultsFound()
        {
            // Arrange
            var fixture = new Fixture();
            var domain = fixture.Create<string>();
            var messageId = fixture.Create<Guid>();

            var dateFrom = DateTimeOffset.UtcNow.AddDays(-1);
            var dateTo = DateTimeOffset.UtcNow;

            // empty cursor (without results)
            var mockCursor = new Mock<IAsyncCursor<BsonDocument>>();
            mockCursor.SetupSequence(c => c.MoveNext(It.IsAny<CancellationToken>()))
                .Returns(true)
                .Returns(false);
            mockCursor.SetupSequence(_ => _.MoveNextAsync(It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult(true))
                .Returns(Task.FromResult(false));
            mockCursor.Setup(_ => _.Current).Returns(Array.Empty<BsonDocument>());

            _mockCollection
                .Setup(c => c.AggregateAsync(It.IsAny<PipelineDefinition<MessageStats, BsonDocument>>(),
                    It.IsAny<AggregateOptions>(), default))
                .ReturnsAsync(mockCursor.Object);

            // Act
            var result = await _repository.GetMessageStatsAsync(domain, messageId, dateFrom, dateTo);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(domain, result.Domain);
            Assert.Equal(messageId, result.MessageId);
            Assert.Equal(dateFrom, result.DateFrom);
            Assert.Equal(dateTo, result.DateTo);
            Assert.Equal(0, result.Sent);
            Assert.Equal(0, result.Delivered);
            Assert.Equal(0, result.NotDelivered);
            Assert.Equal(0, result.Received);
            Assert.Equal(0, result.Click);
            Assert.Equal(0, result.ActionClick);
            Assert.Equal(0, result.BillableSends);

            _mockCollection.Verify(c => c.AggregateAsync(
                It.IsAny<PipelineDefinition<MessageStats, BsonDocument>>(),
                It.IsAny<AggregateOptions>(),
                default), Times.Once);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("00000000-0000-0000-0000-000000000002")]
        public async Task GetMessageStatsByPeriodAsync_ShouldReturnAggregatedStats_WhenSuccess(string messageIdString)
        {
            // Arrange
            var fixture = new Fixture();
            var domain = fixture.Create<string>();

            var messageIds = string.IsNullOrEmpty(messageIdString)
                ? []
                : new List<Guid> { Guid.Parse(messageIdString) };

            var dateFrom = DateTimeOffset.UtcNow.AddDays(-1);
            var dateTo = DateTimeOffset.UtcNow;

            var aggregateResult = new BsonDocument
            {
                { "Date", DateTime.UtcNow },
                { "Sent", 10 },
                { "Delivered", 8 },
                { "NotDelivered", 2 },
                { "Received", 7 },
                { "Click", 3 },
                { "ActionClick", 1 },
                { "BillableSends", 9 }
            };

            var mockCursor = new Mock<IAsyncCursor<BsonDocument>>();

            mockCursor
                .SetupSequence(c => c.MoveNext(It.IsAny<CancellationToken>()))
                .Returns(true)
                .Returns(false);

            mockCursor
                .SetupSequence(c => c.MoveNextAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(true)
                .ReturnsAsync(false);

            mockCursor.Setup(c => c.Current).Returns([aggregateResult]);

            _mockCollection
                .Setup(c => c.AggregateAsync(
                    It.IsAny<PipelineDefinition<MessageStats, BsonDocument>>(),
                    It.IsAny<AggregateOptions>(),
                    default))
                .ReturnsAsync(mockCursor.Object);

            // Act
            var result = await _repository.GetMessageStatsByPeriodAsync(
                domain,
                messageIds,
                dateFrom,
                dateTo,
                "day"
            );

            // Assert
            Assert.NotNull(result);
            Assert.Single(result);

            var item = result.First();

            Assert.Equal(10, item.Sent);
            Assert.Equal(8, item.Delivered);
            Assert.Equal(2, item.NotDelivered);
            Assert.Equal(7, item.Received);
            Assert.Equal(3, item.Click);
            Assert.Equal(1, item.ActionClick);
            Assert.Equal(9, item.BillableSends);

            Assert.True(item.Date <= DateTime.UtcNow);

            _mockCollection.Verify(c => c.AggregateAsync(
                It.IsAny<PipelineDefinition<MessageStats, BsonDocument>>(),
                It.IsAny<AggregateOptions>(),
                default), Times.Once);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public async Task GetMessageStatsByPeriodAsync_ShouldThrowArgumentException_WhenDomainIsInvalid(string domain)
        {
            // Arrange
            var dateFrom = DateTimeOffset.UtcNow.AddDays(-1);
            var dateTo = DateTimeOffset.UtcNow;

            // Act & Assert
            var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
                _repository.GetMessageStatsByPeriodAsync(
                    domain,
                    new List<Guid>(),
                    dateFrom,
                    dateTo,
                    "day"
                )
            );

            Assert.Equal("domain", ex.ParamName);
        }

        [Fact]
        public async Task GetMessageStatsByPeriodAsync_ShouldReturnEmptyList_WhenNoResults()
        {
            // Arrange
            var fixture = new Fixture();
            var domain = fixture.Create<string>();

            var dateFrom = DateTimeOffset.UtcNow.AddDays(-1);
            var dateTo = DateTimeOffset.UtcNow;

            var mockCursor = new Mock<IAsyncCursor<BsonDocument>>();

            mockCursor
                .SetupSequence(c => c.MoveNext(It.IsAny<CancellationToken>()))
                .Returns(true)   // primera iteración
                .Returns(false); // finaliza

            mockCursor
                .SetupSequence(c => c.MoveNextAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(true)
                .ReturnsAsync(false);

            mockCursor.Setup(c => c.Current).Returns(Array.Empty<BsonDocument>());

            _mockCollection
                .Setup(c => c.AggregateAsync(
                    It.IsAny<PipelineDefinition<MessageStats, BsonDocument>>(),
                    It.IsAny<AggregateOptions>(),
                    default))
                .ReturnsAsync(mockCursor.Object);

            // Act
            var result = await _repository.GetMessageStatsByPeriodAsync(
                domain,
                new List<Guid>(),
                dateFrom,
                dateTo,
                "day"
            );

            // Assert
            Assert.NotNull(result);
            Assert.Empty(result);

            _mockCollection.Verify(c => c.AggregateAsync(
                It.IsAny<PipelineDefinition<MessageStats, BsonDocument>>(),
                It.IsAny<AggregateOptions>(),
                default), Times.Once);
        }
    }
}
