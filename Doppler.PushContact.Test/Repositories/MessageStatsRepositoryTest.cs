using AutoFixture;
using Doppler.PushContact.Models.Entities;
using Doppler.PushContact.Repositories;
using Doppler.PushContact.Services;
using Microsoft.Extensions.Options;
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
    }
}
