using AutoFixture;
using Doppler.PushContact.Models.Entities;
using Doppler.PushContact.Services;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Moq;
using System.Collections.Generic;
using System.Threading.Tasks;
using System;
using Xunit;
using Microsoft.Extensions.Logging;
using Doppler.PushContact.DTOs;
using Doppler.PushContact.Repositories;
using System.Threading;
using System.Linq;

namespace Doppler.PushContact.Test.Repositories
{
    public class PushContactRepositoryTest
    {
        private static PushContactRepository CreateSut(
            IMongoClient mongoClient = null,
            IOptions<PushMongoContextSettings> pushMongoContextSettings = null,
            ILogger<PushContactRepository> logger = null)
        {
            return new PushContactRepository(
                mongoClient ?? Mock.Of<IMongoClient>(),
                pushMongoContextSettings ?? Mock.Of<IOptions<PushMongoContextSettings>>(),
                logger ?? Mock.Of<ILogger<PushContactRepository>>());
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public async Task GetAllSubscriptionInfoByDomainAsync_should_throw_argument_exception_when_domain_is_null_or_empty
            (string domain)
        {
            // Arrange
            var sut = CreateSut();

            // Act
            // Assert
            var result = await Assert.ThrowsAsync<ArgumentException>(() => sut.GetAllSubscriptionInfoByDomainAsync(domain));
        }

        [Fact]
        public async Task GetAllSubscriptionInfoByDomainAsync_should_throw_exception_and_log_error_when_subscriptionsinfo_cannot_be_getter_from_storage()
        {
            // Arrange
            var fixture = new Fixture();

            var pushMongoContextSettings = fixture.Create<PushMongoContextSettings>();

            var domain = fixture.Create<string>();

            var pushContactsCollectionMock = new Mock<IMongoCollection<BsonDocument>>();
            pushContactsCollectionMock
                .Setup(x => x.FindAsync(It.IsAny<FilterDefinition<BsonDocument>>(), It.IsAny<FindOptions<BsonDocument, BsonDocument>>(), default))
                .ThrowsAsync(new Exception());

            var mongoDatabaseMock = new Mock<IMongoDatabase>();
            mongoDatabaseMock
                .Setup(x => x.GetCollection<BsonDocument>(pushMongoContextSettings.PushContactsCollectionName, null))
                .Returns(pushContactsCollectionMock.Object);

            var mongoClientMock = new Mock<IMongoClient>();
            mongoClientMock
                .Setup(x => x.GetDatabase(pushMongoContextSettings.DatabaseName, null))
                .Returns(mongoDatabaseMock.Object);

            var loggerMock = new Mock<ILogger<PushContactRepository>>();

            var sut = CreateSut(
                mongoClientMock.Object,
                Options.Create(pushMongoContextSettings),
                logger: loggerMock.Object);

            // Act
            // Assert
            await Assert.ThrowsAsync<Exception>(() => sut.GetAllSubscriptionInfoByDomainAsync(domain));

            loggerMock.Verify(
                x => x.Log(
                    It.Is<LogLevel>(l => l == LogLevel.Error),
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString() == $"Error getting {nameof(SubscriptionInfoDTO)}s by {nameof(domain)} {domain}"),
                    It.IsAny<Exception>(),
                    It.Is<Func<It.IsAnyType, Exception, string>>((v, t) => true)),
                Times.Once);
        }

        [Fact]
        public async Task GetAllSubscriptionInfoByDomainAsync_should_return_subscriptionsinfo_filtered_by_domain()
        {
            // Arrange
            List<BsonDocument> allPushContactDocuments = FakePushContactDocuments(10);

            var random = new Random();
            int randomPushContactIndex = random.Next(allPushContactDocuments.Count);
            var domainFilter = allPushContactDocuments[randomPushContactIndex][PushContactDocumentProps.DomainPropName].AsString;

            var fixture = new Fixture();

            var pushMongoContextSettings = fixture.Create<PushMongoContextSettings>();

            var pushContactsCursorMock = new Mock<IAsyncCursor<BsonDocument>>();
            pushContactsCursorMock
                .Setup(_ => _.Current)
                .Returns(allPushContactDocuments.Where(x => x[PushContactDocumentProps.DomainPropName].AsString == domainFilter));

            pushContactsCursorMock
                .SetupSequence(_ => _.MoveNext(It.IsAny<CancellationToken>()))
                .Returns(true)
                .Returns(false);

            pushContactsCursorMock
                .SetupSequence(_ => _.MoveNextAsync(It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult(true))
                .Returns(Task.FromResult(false));

            var pushContactsCollectionMock = new Mock<IMongoCollection<BsonDocument>>();
            pushContactsCollectionMock
                .Setup(x => x.FindAsync(It.IsAny<FilterDefinition<BsonDocument>>(), It.IsAny<FindOptions<BsonDocument, BsonDocument>>(), default))
                .ReturnsAsync(pushContactsCursorMock.Object);

            var mongoDatabase = new Mock<IMongoDatabase>();
            mongoDatabase
                .Setup(x => x.GetCollection<BsonDocument>(pushMongoContextSettings.PushContactsCollectionName, null))
                .Returns(pushContactsCollectionMock.Object);

            var mongoClient = new Mock<IMongoClient>();
            mongoClient
                .Setup(x => x.GetDatabase(pushMongoContextSettings.DatabaseName, null))
                .Returns(mongoDatabase.Object);

            var sut = CreateSut(
                mongoClient.Object,
                Options.Create(pushMongoContextSettings));

            // Act
            var result = await sut.GetAllSubscriptionInfoByDomainAsync(domainFilter);

            // Assert
            Assert.All(result, res => allPushContactDocuments
                    .Single(p => p[PushContactDocumentProps.DomainPropName].AsString == domainFilter &&
                        p[PushContactDocumentProps.DeviceTokenPropName].AsString == res.DeviceToken &&
                        p[PushContactDocumentProps.Subscription_PropName][PushContactDocumentProps.Subscription_EndPoint_PropName] == res.Subscription.EndPoint &&
                        p[PushContactDocumentProps.Subscription_PropName][PushContactDocumentProps.Subscription_P256DH_PropName] == res.Subscription.Keys.P256DH &&
                        p[PushContactDocumentProps.Subscription_PropName][PushContactDocumentProps.Subscription_Auth_PropName] == res.Subscription.Keys.Auth
                    )
                );
        }

        private static BsonDocument FakeSubscriptionDocument()
        {
            var fixture = new Fixture();
            return new BsonDocument {
                { PushContactDocumentProps.Subscription_EndPoint_PropName, fixture.Create<string>() },
                { PushContactDocumentProps.Subscription_Auth_PropName, fixture.Create<string>() },
                { PushContactDocumentProps.Subscription_P256DH_PropName, fixture.Create<string>() },
            };
        }

        private static List<BsonDocument> FakePushContactDocuments(int count)
        {
            var fixture = new Fixture();

            return Enumerable.Repeat(0, count)
                .Select(x =>
                {
                    return new BsonDocument {
                            { PushContactDocumentProps.IdPropName, fixture.Create<string>() },
                            { PushContactDocumentProps.DomainPropName, fixture.Create<string>() },
                            { PushContactDocumentProps.DeviceTokenPropName, fixture.Create<string>() },
                            { PushContactDocumentProps.VisitorGuidPropName, fixture.Create<string>() },
                            { PushContactDocumentProps.EmailPropName, fixture.Create<string>() },
                            { PushContactDocumentProps.ModifiedPropName, fixture.Create<DateTime>().ToUniversalTime() },
                            { PushContactDocumentProps.DeletedPropName, fixture.Create<bool>() },
                            { PushContactDocumentProps.Subscription_PropName, FakeSubscriptionDocument() },
                    };
                })
                .ToList();
        }
    }
}
