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

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public async Task GetSubscriptionInfoByDomainAsStreamAsync_should_throw_argument_exception_when_domain_is_null_or_empty
            (string domain)
        {
            // Arrange
            var sut = CreateSut();

            // Act & Assert
            var ex = await Assert.ThrowsAsync<ArgumentException>(async () =>
            {
                await foreach (var _ in sut.GetSubscriptionInfoByDomainAsStreamAsync(domain)) { }
            });
        }

        [Fact]
        public async Task GetSubscriptionInfoByDomainAsStreamAsync_should_log_warning_when_cancelled()
        {
            // Arrange
            var loggerMock = new Mock<ILogger<PushContactRepository>>();
            var cts = new CancellationTokenSource();
            cts.Cancel(); // simulate cancelled

            var cursorMock = new Mock<IAsyncCursor<BsonDocument>>();
            cursorMock.SetupSequence(c => c.MoveNextAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
            cursorMock.SetupGet(c => c.Current).Returns(new List<BsonDocument> { new BsonDocument() });

            var collectionMock = new Mock<IMongoCollection<BsonDocument>>();
            collectionMock.Setup(c => c.FindAsync(It.IsAny<FilterDefinition<BsonDocument>>(), It.IsAny<FindOptions<BsonDocument>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(cursorMock.Object);

            var mongoDatabaseMock = new Mock<IMongoDatabase>();
            mongoDatabaseMock.Setup(d => d.GetCollection<BsonDocument>(It.IsAny<string>(), null)).Returns(collectionMock.Object);

            var mongoClientMock = new Mock<IMongoClient>();
            mongoClientMock.Setup(c => c.GetDatabase(It.IsAny<string>(), null)).Returns(mongoDatabaseMock.Object);

            var sut = CreateSut(
                mongoClientMock.Object,
                Options.Create(new PushMongoContextSettings()),
                logger: loggerMock.Object
            );

            // Act
            await foreach (var _ in sut.GetSubscriptionInfoByDomainAsStreamAsync("test-domain", cts.Token)) { }

            // Assert
            loggerMock.Verify(
                x => x.Log(
                    LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Streaming cancelled")),
                    null,
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                Times.Once);
        }

        [Fact]
        public async Task GetSubscriptionInfoByDomainAsStreamAsync_should_yield_results()
        {
            // Arrange
            var doc = new BsonDocument
            {
                { PushContactDocumentProps.DeviceTokenPropName, "dev-token" },
                { PushContactDocumentProps.IdPropName, ObjectId.GenerateNewId() },
                { PushContactDocumentProps.Subscription_PropName, new BsonDocument {
                    { PushContactDocumentProps.Subscription_EndPoint_PropName, "endpoint" },
                    { PushContactDocumentProps.Subscription_Auth_PropName, "auth" },
                    { PushContactDocumentProps.Subscription_P256DH_PropName, "p256dh" }
                }}
            };

            var cursorMock = new Mock<IAsyncCursor<BsonDocument>>();
            cursorMock.SetupSequence(c => c.MoveNextAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(true)
                .ReturnsAsync(false);
            cursorMock.SetupGet(c => c.Current).Returns(new List<BsonDocument> { doc });

            var collectionMock = new Mock<IMongoCollection<BsonDocument>>();
            collectionMock.Setup(c => c.FindAsync(It.IsAny<FilterDefinition<BsonDocument>>(), It.IsAny<FindOptions<BsonDocument>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(cursorMock.Object);

            var mongoDatabaseMock = new Mock<IMongoDatabase>();
            mongoDatabaseMock.Setup(d => d.GetCollection<BsonDocument>(It.IsAny<string>(), null)).Returns(collectionMock.Object);

            var mongoClientMock = new Mock<IMongoClient>();
            mongoClientMock.Setup(c => c.GetDatabase(It.IsAny<string>(), null)).Returns(mongoDatabaseMock.Object);

            var sut = CreateSut(
                mongoClientMock.Object,
                Options.Create(new PushMongoContextSettings()),
                logger: Mock.Of<ILogger<PushContactRepository>>()
            );

            // Act
            var results = new List<SubscriptionInfoDTO>();
            await foreach (var item in sut.GetSubscriptionInfoByDomainAsStreamAsync("test-domain"))
            {
                results.Add(item);
            }

            // Assert
            Assert.Single(results);
            Assert.Equal("dev-token", results[0].DeviceToken);
            Assert.NotNull(results[0].Subscription);
            Assert.Equal("endpoint", results[0].Subscription.EndPoint);
        }

        [Fact]
        public async Task GetSubscriptionInfoByDomainAsStreamAsync_should_return_Null_subscription_when_subscription_is_incomplete()
        {
            // Arrange
            var doc = new BsonDocument
            {
                { PushContactDocumentProps.DeviceTokenPropName, "incomplete-subscription-token" },
                { PushContactDocumentProps.IdPropName, ObjectId.GenerateNewId() },
                { PushContactDocumentProps.Subscription_PropName, new BsonDocument {
                    // intentionally missing required fields like auth and p256dh
                    { PushContactDocumentProps.Subscription_EndPoint_PropName, "endpoint" }
                }}
            };

            var cursorMock = new Mock<IAsyncCursor<BsonDocument>>();
            cursorMock.SetupSequence(c => c.MoveNextAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(true)
                .ReturnsAsync(false);
            cursorMock.SetupGet(c => c.Current).Returns(new List<BsonDocument> { doc });

            var collectionMock = new Mock<IMongoCollection<BsonDocument>>();
            collectionMock.Setup(c => c.FindAsync(It.IsAny<FilterDefinition<BsonDocument>>(), It.IsAny<FindOptions<BsonDocument>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(cursorMock.Object);

            var mongoDatabaseMock = new Mock<IMongoDatabase>();
            mongoDatabaseMock.Setup(d => d.GetCollection<BsonDocument>(It.IsAny<string>(), null)).Returns(collectionMock.Object);

            var mongoClientMock = new Mock<IMongoClient>();
            mongoClientMock.Setup(c => c.GetDatabase(It.IsAny<string>(), null)).Returns(mongoDatabaseMock.Object);

            var sut = CreateSut(
                mongoClientMock.Object,
                Options.Create(new PushMongoContextSettings()),
                logger: Mock.Of<ILogger<PushContactRepository>>()
            );

            // Act
            var results = new List<SubscriptionInfoDTO>();
            await foreach (var item in sut.GetSubscriptionInfoByDomainAsStreamAsync("test-domain"))
            {
                results.Add(item);
            }

            // Assert
            Assert.Single(results);
            var result = results[0];
            Assert.Equal("incomplete-subscription-token", result.DeviceToken);
            Assert.Null(result.Subscription); // should be null because the subscription is not complete
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public async Task GetAllSubscriptionInfoByVisitorGuidAsync_should_throw_argument_exception_when_visitorGuid_is_null_or_empty
            (string visitorGuid)
        {
            // Arrange
            var sut = CreateSut();

            // Act
            // Assert
            var result = await Assert.ThrowsAsync<ArgumentException>(() => sut.GetAllSubscriptionInfoByVisitorGuidAsync(visitorGuid));
        }

        [Fact]
        public async Task GetAllSubscriptionInfoByVisitorGuidAsync_should_throw_exception_and_log_error_when_subscriptionsinfo_cannot_be_getter_from_storage()
        {
            // Arrange
            var fixture = new Fixture();

            var pushMongoContextSettings = fixture.Create<PushMongoContextSettings>();

            var visitorGuid = fixture.Create<string>();

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
            await Assert.ThrowsAsync<Exception>(() => sut.GetAllSubscriptionInfoByVisitorGuidAsync(visitorGuid));

            loggerMock.Verify(
                x => x.Log(
                    It.Is<LogLevel>(l => l == LogLevel.Error),
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString() == $"Error getting {nameof(SubscriptionInfoDTO)}s by {nameof(visitorGuid)} {visitorGuid}"),
                    It.IsAny<Exception>(),
                    It.Is<Func<It.IsAnyType, Exception, string>>((v, t) => true)),
                Times.Once);
        }

        [Fact]
        public async Task GetAllSubscriptionInfoByVisitorGuidAsync_should_return_subscriptionsinfo_filtered_by_visitorGuid()
        {
            // Arrange
            List<BsonDocument> allPushContactDocuments = FakePushContactDocuments(10);

            var random = new Random();
            int randomPushContactIndex = random.Next(allPushContactDocuments.Count);
            var visitorGuidFilter = allPushContactDocuments[randomPushContactIndex][PushContactDocumentProps.VisitorGuidPropName].AsString;

            var fixture = new Fixture();

            var pushMongoContextSettings = fixture.Create<PushMongoContextSettings>();

            var pushContactsCursorMock = new Mock<IAsyncCursor<BsonDocument>>();
            pushContactsCursorMock
                .Setup(_ => _.Current)
                .Returns(allPushContactDocuments.Where(x => x[PushContactDocumentProps.VisitorGuidPropName].AsString == visitorGuidFilter));

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
            var result = await sut.GetAllSubscriptionInfoByVisitorGuidAsync(visitorGuidFilter);

            // Assert
            Assert.All(result, res => allPushContactDocuments
                    .Single(p => p[PushContactDocumentProps.VisitorGuidPropName].AsString == visitorGuidFilter &&
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
