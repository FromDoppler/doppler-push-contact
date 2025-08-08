using AutoFixture;
using Doppler.PushContact.Models;
using Doppler.PushContact.Models.DTOs;
using Doppler.PushContact.Repositories;
using Doppler.PushContact.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
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
    public class DomainRepositoryTest
    {
        private static DomainRepository CreateSut(
            IMongoClient mongoClient = null,
            IOptions<PushMongoContextSettings> pushMongoContextSettings = null,
            ILogger<DomainRepository> logger = null)
        {
            return new DomainRepository(
                mongoClient ?? Mock.Of<IMongoClient>(),
                pushMongoContextSettings ?? Mock.Of<IOptions<PushMongoContextSettings>>(),
                logger ?? Mock.Of<ILogger<DomainRepository>>());
        }

        private static List<BsonDocument> FakeDomainsDocuments(int count)
        {
            var fixture = new Fixture();

            return Enumerable.Repeat(0, count)
                .Select(x =>
                {
                    return new BsonDocument {
                            { DomainDocumentProps.IdPropName, fixture.Create<string>() },
                            { DomainDocumentProps.DomainNamePropName, fixture.Create<string>() },
                            { DomainDocumentProps.IsPushFeatureEnabledPropName, fixture.Create<bool>() },
                            { DomainDocumentProps.UsesExternalPushDomain, fixture.Create<bool>() },
                            { DomainDocumentProps.ExternalPushDomain, fixture.Create<string>() },
                            { DomainDocumentProps.ModifiedPropName, fixture.Create<DateTime>().ToUniversalTime() },
                    };
                })
                .ToList();
        }

        [Fact]
        public async Task UpsertAsync_should_throw_argument_null_exception_when_domain_is_null()
        {
            // Arrange
            DomainDTO domain = null;

            var sut = CreateSut();

            // Act
            // Assert
            var result = await Assert.ThrowsAsync<ArgumentNullException>(() => sut.UpsertAsync(domain));
        }

        [Fact]
        public async Task UpsertAsync_should_throw_exception_and_log_error_when_a_domain_cannot_be_upserted()
        {
            // Arrange
            var fixture = new Fixture();

            var domain = fixture.Create<DomainDTO>();

            var pushMongoContextSettings = fixture.Create<PushMongoContextSettings>();

            var domainsCollectionMock = new Mock<IMongoCollection<BsonDocument>>();
            domainsCollectionMock
                .Setup(x => x.UpdateOneAsync(It.IsAny<FilterDefinition<BsonDocument>>(), It.IsAny<UpdateDefinition<BsonDocument>>(), It.IsAny<UpdateOptions>(), default))
                .ThrowsAsync(new Exception());

            var mongoDatabaseMock = new Mock<IMongoDatabase>();
            mongoDatabaseMock
                .Setup(x => x.GetCollection<BsonDocument>(pushMongoContextSettings.DomainsCollectionName, null))
                .Returns(domainsCollectionMock.Object);

            var mongoClientMock = new Mock<IMongoClient>();
            mongoClientMock
                .Setup(x => x.GetDatabase(pushMongoContextSettings.DatabaseName, null))
                .Returns(mongoDatabaseMock.Object);

            var loggerMock = new Mock<ILogger<DomainRepository>>();

            var sut = CreateSut(
                mongoClientMock.Object,
                Options.Create(pushMongoContextSettings),
                loggerMock.Object);

            // Act
            // Assert
            await Assert.ThrowsAsync<Exception>(() => sut.UpsertAsync(domain));

            loggerMock.Verify(
                x => x.Log(
                    It.Is<LogLevel>(l => l == LogLevel.Error),
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString() == $"Error upserting {nameof(Domain)} with {nameof(domain.Name)} {domain.Name}"),
                    It.IsAny<Exception>(),
                    It.Is<Func<It.IsAnyType, Exception, string>>((v, t) => true)),
                Times.Once);
        }

        [Fact]
        public async Task UpsertAsync_should_not_throw_exception_when_a_domain_can_be_upserted()
        {
            // Arrange
            var fixture = new Fixture();

            var domain = fixture.Create<DomainDTO>();

            var pushMongoContextSettings = fixture.Create<PushMongoContextSettings>();

            var updateResultMock = new Mock<UpdateResult>();
            var domainsCollection = new Mock<IMongoCollection<BsonDocument>>();
            domainsCollection
                .Setup(x => x.UpdateOneAsync(It.IsAny<FilterDefinition<BsonDocument>>(), It.IsAny<UpdateDefinition<BsonDocument>>(), It.IsAny<UpdateOptions>(), default))
                .ReturnsAsync(updateResultMock.Object);

            var mongoDatabase = new Mock<IMongoDatabase>();
            mongoDatabase
                .Setup(x => x.GetCollection<BsonDocument>(pushMongoContextSettings.DomainsCollectionName, null))
                .Returns(domainsCollection.Object);

            var mongoClient = new Mock<IMongoClient>();
            mongoClient
                .Setup(x => x.GetDatabase(pushMongoContextSettings.DatabaseName, null))
                .Returns(mongoDatabase.Object);

            var sut = CreateSut(
                mongoClient.Object,
                Options.Create(pushMongoContextSettings));

            // Act
            // Assert
            await sut.UpsertAsync(domain);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData(" ")]
        [InlineData(" \n ")]
        [InlineData("\t")]
        [InlineData("\r")]
        public async Task GetByNameAsync_should_throw_argument_exception_when_domain_name_is_null_or_whitespace(string name)
        {
            // Arrange
            var sut = CreateSut();

            // Act
            // Assert
            var result = await Assert.ThrowsAsync<ArgumentException>(() => sut.GetByNameAsync(name));
        }

        [Fact]
        public async Task GetByNameAsync_should_return_null_when_domains_documents_are_empty()
        {
            // Arrange
            var fixture = new Fixture();

            var domainsCursorMock = new Mock<IAsyncCursor<BsonDocument>>();
            domainsCursorMock
                .Setup(_ => _.Current)
                .Returns(Enumerable.Empty<BsonDocument>());

            var domainsCollectionMock = new Mock<IMongoCollection<BsonDocument>>();
            domainsCollectionMock
                .Setup(x => x.FindAsync(It.IsAny<FilterDefinition<BsonDocument>>(), It.IsAny<FindOptions<BsonDocument, BsonDocument>>(), default))
                .ReturnsAsync(domainsCursorMock.Object);

            var pushMongoContextSettings = fixture.Create<PushMongoContextSettings>();
            var mongoDatabase = new Mock<IMongoDatabase>();
            mongoDatabase
                .Setup(x => x.GetCollection<BsonDocument>(pushMongoContextSettings.DomainsCollectionName, null))
                .Returns(domainsCollectionMock.Object);

            var mongoClient = new Mock<IMongoClient>();
            mongoClient
                .Setup(x => x.GetDatabase(pushMongoContextSettings.DatabaseName, null))
                .Returns(mongoDatabase.Object);

            var sut = CreateSut(
                mongoClient.Object,
                Options.Create(pushMongoContextSettings));

            // Act
            var result = await sut.GetByNameAsync(fixture.Create<string>());

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public async Task GetByNameAsync_should_return_domain_OK()
        {
            // Arrange
            var fixture = new Fixture();

            List<BsonDocument> allDomainDocuments = FakeDomainsDocuments(5);

            var random = new Random();
            int randomPushContactIndex = random.Next(allDomainDocuments.Count);
            var domainDocument = allDomainDocuments[randomPushContactIndex];
            var domainFilter = domainDocument[DomainDocumentProps.DomainNamePropName].AsString;

            var domainExpected = new Domain()
            {
                Name = domainFilter,
                IsPushFeatureEnabled = domainDocument[DomainDocumentProps.IsPushFeatureEnabledPropName].AsBoolean,
                UsesExternalPushDomain = domainDocument[DomainDocumentProps.UsesExternalPushDomain].AsBoolean,
                ExternalPushDomain = domainDocument[DomainDocumentProps.ExternalPushDomain].AsString,
            };

            var pushMongoContextSettings = fixture.Create<PushMongoContextSettings>();

            var domainsCursorMock = new Mock<IAsyncCursor<BsonDocument>>();
            domainsCursorMock
                .Setup(_ => _.Current)
                .Returns(allDomainDocuments.Where(x => x[DomainDocumentProps.DomainNamePropName].AsString == domainFilter));

            domainsCursorMock
                .SetupSequence(_ => _.MoveNextAsync(It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult(true)) // first calling to .MoveNextAsync(), returns true (there are results)
                .Returns(Task.FromResult(false)); // the next time, returns false (there are not more results)

            var domainsCollectionMock = new Mock<IMongoCollection<BsonDocument>>();
            domainsCollectionMock
                .Setup(x => x.FindAsync(It.IsAny<FilterDefinition<BsonDocument>>(), It.IsAny<FindOptions<BsonDocument, BsonDocument>>(), default))
                .ReturnsAsync(domainsCursorMock.Object);

            var mongoDatabase = new Mock<IMongoDatabase>();
            mongoDatabase
                .Setup(x => x.GetCollection<BsonDocument>(pushMongoContextSettings.DomainsCollectionName, null))
                .Returns(domainsCollectionMock.Object);

            var mongoClient = new Mock<IMongoClient>();
            mongoClient
                .Setup(x => x.GetDatabase(pushMongoContextSettings.DatabaseName, null))
                .Returns(mongoDatabase.Object);

            var sut = CreateSut(
                mongoClient.Object,
                Options.Create(pushMongoContextSettings));

            // Act
            var result = await sut.GetByNameAsync(fixture.Create<string>());

            // Assert
            Assert.NotNull(result);
            Assert.Equal(domainExpected.Name, result.Name);
            Assert.Equal(domainExpected.IsPushFeatureEnabled, result.IsPushFeatureEnabled);
            Assert.Equal(domainExpected.UsesExternalPushDomain, result.UsesExternalPushDomain);
            Assert.Equal(domainExpected.ExternalPushDomain, result.ExternalPushDomain);
        }

        [Fact]
        public async Task GetByNameAsync_should_log_and_throw_when_exception_occurs()
        {
            // Arrange
            var fixture = new Fixture();
            var domainName = fixture.Create<string>();

            var pushMongoContextSettings = fixture.Create<PushMongoContextSettings>();

            var loggerMock = new Mock<ILogger<DomainRepository>>();

            var mongoCollectionMock = new Mock<IMongoCollection<BsonDocument>>();
            mongoCollectionMock
                .Setup(x => x.FindAsync(It.IsAny<FilterDefinition<BsonDocument>>(), It.IsAny<FindOptions<BsonDocument, BsonDocument>>(), default))
                .ThrowsAsync(new Exception("DB error"));

            var mongoDatabaseMock = new Mock<IMongoDatabase>();
            mongoDatabaseMock
                .Setup(x => x.GetCollection<BsonDocument>(pushMongoContextSettings.DomainsCollectionName, null))
                .Returns(mongoCollectionMock.Object);

            var mongoClientMock = new Mock<IMongoClient>();
            mongoClientMock
                .Setup(x => x.GetDatabase(pushMongoContextSettings.DatabaseName, null))
                .Returns(mongoDatabaseMock.Object);

            var sut = CreateSut(
                mongoClientMock.Object,
                Options.Create(pushMongoContextSettings),
                loggerMock.Object);

            // Act
            var exception = await Assert.ThrowsAsync<Exception>(() =>
                sut.GetByNameAsync(domainName));

            // Assert
            Assert.Equal("DB error", exception.Message);

            loggerMock.Verify(x =>
                x.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((state, t) =>
                        state.ToString().Contains($"Error getting Domain with name equals to {domainName}")),
                    It.Is<Exception>(ex => ex.Message == "DB error"),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                Times.Once);
        }
    }
}
