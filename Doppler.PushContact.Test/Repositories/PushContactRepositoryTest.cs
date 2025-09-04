using AutoFixture;
using Doppler.PushContact.DTOs;
using Doppler.PushContact.Models;
using Doppler.PushContact.Models.Entities;
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
            Fixture fixture = new Fixture();
            var domain = fixture.Create<string>();

            var sut = CreateSut();

            // Act
            // Assert
            var result = await Assert.ThrowsAsync<ArgumentException>(() => sut.GetAllSubscriptionInfoByVisitorGuidAsync(domain, visitorGuid));
        }

        [Fact]
        public async Task GetAllSubscriptionInfoByVisitorGuidAsync_should_throw_exception_and_log_error_when_subscriptionsinfo_cannot_be_getter_from_storage()
        {
            // Arrange
            var fixture = new Fixture();

            var pushMongoContextSettings = fixture.Create<PushMongoContextSettings>();

            var visitorGuid = fixture.Create<string>();
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
            await Assert.ThrowsAsync<Exception>(() => sut.GetAllSubscriptionInfoByVisitorGuidAsync(domain, visitorGuid));

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

            Fixture fixture = new Fixture();
            var domain = fixture.Create<string>();

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
            var result = await sut.GetAllSubscriptionInfoByVisitorGuidAsync(domain, visitorGuidFilter);

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

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public async Task GetContactsStatsAsync_should_throw_argument_exception_when_domain_is_null_or_empty(string domainName)
        {
            // Arrange
            var sut = CreateSut();

            // Act & Assert
            var ex = await Assert.ThrowsAsync<ArgumentException>(() => sut.GetContactsStatsAsync(domainName));
            Assert.Equal("domainName", ex.ParamName);
        }

        [Fact]
        public async Task GetContactsStatsAsync_should_return_stats_ok_when_contacts_exist_for_domain()
        {
            // Arrange
            var fixture = new Fixture();
            var domain = fixture.Create<string>();

            var expectedDocs = new[]
            {
                new BsonDocument { { "_id", false }, { "count", 5 } },
                new BsonDocument { { "_id", true }, { "count", 2 } }
            };

            var cursorMock = new Mock<IAsyncCursor<BsonDocument>>();
            cursorMock.SetupSequence(x => x.MoveNextAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(true)   // first call: return results
                .ReturnsAsync(false); // second call: finish iteration
            cursorMock.SetupGet(x => x.Current).Returns(expectedDocs);

            var pushContactsCollectionMock = new Mock<IMongoCollection<BsonDocument>>();
            pushContactsCollectionMock
                .Setup(x => x.AggregateAsync<BsonDocument>(
                    It.IsAny<PipelineDefinition<BsonDocument, BsonDocument>>(),
                    It.IsAny<AggregateOptions>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(cursorMock.Object);

            var pushMongoContextSettings = fixture.Create<PushMongoContextSettings>();

            var mongoDatabaseMock = new Mock<IMongoDatabase>();
            mongoDatabaseMock
                .Setup(x => x.GetCollection<BsonDocument>(pushMongoContextSettings.PushContactsCollectionName, null))
                .Returns(pushContactsCollectionMock.Object);

            var mongoClientMock = new Mock<IMongoClient>();
            mongoClientMock
                .Setup(x => x.GetDatabase(pushMongoContextSettings.DatabaseName, null))
                .Returns(mongoDatabaseMock.Object);

            var sut = CreateSut(mongoClientMock.Object, Options.Create(pushMongoContextSettings));

            // Act
            var result = await sut.GetContactsStatsAsync(domain);

            // Assert
            Assert.Equal(domain, result.DomainName);
            Assert.Equal(5, result.Active);
            Assert.Equal(2, result.Deleted);
            Assert.Equal(7, result.Total);
        }

        [Fact]
        public async Task GetContactsStatsAsync_should_return_zero_counts_when_no_contacts_found()
        {
            // Arrange
            var fixture = new Fixture();
            var domain = fixture.Create<string>();

            var emptyDocs = new List<BsonDocument>();

            var cursorMock = new Mock<IAsyncCursor<BsonDocument>>();
            cursorMock.SetupSequence(x => x.MoveNextAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(true)   // first call: return results
                .ReturnsAsync(false); // second call: finish iteration
            cursorMock.SetupGet(x => x.Current).Returns(emptyDocs);

            var pushContactsCollectionMock = new Mock<IMongoCollection<BsonDocument>>();
            pushContactsCollectionMock
                .Setup(x => x.AggregateAsync<BsonDocument>(
                    It.IsAny<PipelineDefinition<BsonDocument, BsonDocument>>(),
                    It.IsAny<AggregateOptions>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(cursorMock.Object);

            var pushMongoContextSettings = fixture.Create<PushMongoContextSettings>();

            var mongoDatabaseMock = new Mock<IMongoDatabase>();
            mongoDatabaseMock
                .Setup(x => x.GetCollection<BsonDocument>(pushMongoContextSettings.PushContactsCollectionName, null))
                .Returns(pushContactsCollectionMock.Object);

            var mongoClientMock = new Mock<IMongoClient>();
            mongoClientMock
                .Setup(x => x.GetDatabase(pushMongoContextSettings.DatabaseName, null))
                .Returns(mongoDatabaseMock.Object);

            var sut = CreateSut(mongoClientMock.Object, Options.Create(pushMongoContextSettings));

            // Act
            var result = await sut.GetContactsStatsAsync(domain);

            // Assert
            Assert.Equal(domain, result.DomainName);
            Assert.Equal(0, result.Active);
            Assert.Equal(0, result.Deleted);
            Assert.Equal(0, result.Total);
        }

        [Fact]
        public async Task GetContactsStatsAsync_should_log_error_and_throw_when_exception_occurs()
        {
            // Arrange
            var fixture = new Fixture();
            var domain = fixture.Create<string>();

            var pushContactsCollectionMock = new Mock<IMongoCollection<BsonDocument>>();
            pushContactsCollectionMock
                .Setup(x => x.AggregateAsync<BsonDocument>(
                    It.IsAny<PipelineDefinition<BsonDocument, BsonDocument>>(),
                    It.IsAny<AggregateOptions>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new Exception("Aggregation failed"));

            var pushMongoContextSettings = fixture.Create<PushMongoContextSettings>();

            var mongoDatabaseMock = new Mock<IMongoDatabase>();
            mongoDatabaseMock
                .Setup(x => x.GetCollection<BsonDocument>(pushMongoContextSettings.PushContactsCollectionName, null))
                .Returns(pushContactsCollectionMock.Object);

            var mongoClientMock = new Mock<IMongoClient>();
            mongoClientMock
                .Setup(x => x.GetDatabase(pushMongoContextSettings.DatabaseName, null))
                .Returns(mongoDatabaseMock.Object);

            var loggerMock = new Mock<ILogger<PushContactRepository>>();

            var sut = CreateSut(mongoClientMock.Object, Options.Create(pushMongoContextSettings), loggerMock.Object);

            // Act & Assert
            await Assert.ThrowsAsync<Exception>(() => sut.GetContactsStatsAsync(domain));

            loggerMock.Verify(
                x => x.Log(
                    It.Is<LogLevel>(l => l == LogLevel.Error),
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains($"Error getting contact stats for domain '{domain}'")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                Times.Once);
        }

        [Fact]
        public async Task GetVisitorInfoSafeAsync_should_return_null_when_push_contact_does_not_exist()
        {
            // Arrange
            var fixture = new Fixture();
            var deviceToken = fixture.Create<string>();

            var mongoClientMock = new Mock<IMongoClient>();
            var databaseMock = new Mock<IMongoDatabase>();
            var collectionMock = new Mock<IMongoCollection<BsonDocument>>();
            var asyncCursorMock = new Mock<IAsyncCursor<BsonDocument>>();

            var settings = Options.Create(new PushMongoContextSettings
            {
                DatabaseName = "TestDatabase",
                PushContactsCollectionName = "TestCollection"
            });

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

            databaseMock
                .Setup(x => x.GetCollection<BsonDocument>(settings.Value.PushContactsCollectionName, null))
                .Returns(collectionMock.Object);

            mongoClientMock
                .Setup(x => x.GetDatabase(settings.Value.DatabaseName, null))
                .Returns(databaseMock.Object);

            var sut = CreateSut(
                mongoClientMock.Object,
                settings);

            // Act
            var result = await sut.GetVisitorInfoSafeAsync(deviceToken);

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public async Task GetVisitorInfoSafeAsync_should_return_VisitorInfo_when_push_contact_exists()
        {
            // Arrange
            var fixture = new Fixture();
            var deviceToken = fixture.Create<string>();

            var domain = fixture.Create<string>();
            var visitorGuid = fixture.Create<string>();
            var email = fixture.Create<string>();

            var document = new BsonDocument
            {
                { PushContactDocumentProps.DomainPropName, domain },
                { PushContactDocumentProps.VisitorGuidPropName, visitorGuid },
                { PushContactDocumentProps.EmailPropName, email },
            };

            var mongoClientMock = new Mock<IMongoClient>();
            var databaseMock = new Mock<IMongoDatabase>();
            var collectionMock = new Mock<IMongoCollection<BsonDocument>>();
            var asyncCursorMock = new Mock<IAsyncCursor<BsonDocument>>();

            var settings = Options.Create(new PushMongoContextSettings
            {
                DatabaseName = "TestDatabase",
                PushContactsCollectionName = "TestCollection"
            });

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

            databaseMock
                .Setup(x => x.GetCollection<BsonDocument>(settings.Value.PushContactsCollectionName, null))
                .Returns(collectionMock.Object);

            mongoClientMock
                .Setup(x => x.GetDatabase(settings.Value.DatabaseName, null))
                .Returns(databaseMock.Object);

            var sut = CreateSut(
                mongoClientMock.Object,
                settings);

            // Act
            var visitorInfo = await sut.GetVisitorInfoSafeAsync(deviceToken);

            // Assert
            Assert.Equal(domain, visitorInfo.Domain);
            Assert.Equal(visitorGuid, visitorInfo.VisitorGuid);
            Assert.Equal(email, visitorInfo.Email);
        }

        [Fact]
        public async Task GetVisitorInfoSafeAsync_should_return_null_and_log_error_when_mongo_exception_is_thrown()
        {
            // Arrange
            var fixture = new Fixture();
            var deviceToken = fixture.Create<string>();

            var mongoClientMock = new Mock<IMongoClient>();
            var databaseMock = new Mock<IMongoDatabase>();
            var collectionMock = new Mock<IMongoCollection<BsonDocument>>();
            var loggerMock = new Mock<ILogger<PushContactRepository>>();

            var settings = Options.Create(new PushMongoContextSettings
            {
                DatabaseName = "TestDatabase",
                PushContactsCollectionName = "TestCollection"
            });

            collectionMock
                .Setup(x => x.FindAsync(It.IsAny<FilterDefinition<BsonDocument>>(), It.IsAny<FindOptions<BsonDocument, BsonDocument>>(), default))
                .Throws(new MongoException("Test exception"));

            databaseMock
                .Setup(x => x.GetCollection<BsonDocument>(settings.Value.PushContactsCollectionName, null))
                .Returns(collectionMock.Object);

            mongoClientMock
                .Setup(x => x.GetDatabase(settings.Value.DatabaseName, null))
                .Returns(databaseMock.Object);

            var sut = CreateSut(
                mongoClientMock.Object,
                settings,
                logger: loggerMock.Object);

            // Act
            var visitorInfo = await sut.GetVisitorInfoSafeAsync(deviceToken);

            // Assert
            Assert.Null(visitorInfo);
            loggerMock.Verify(
                x => x.Log(
                    It.Is<LogLevel>(l => l == LogLevel.Error),
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("MongoException getting Visitor Info by")),
                    It.IsAny<Exception>(),
                    It.Is<Func<It.IsAnyType, Exception, string>>((v, t) => true)),
                Times.Once);
        }

        [Fact]
        public async Task GetVisitorInfoSafeAsync_should_return_null_and_log_error_when_general_exception_is_thrown()
        {
            // Arrange
            var fixture = new Fixture();
            var deviceToken = fixture.Create<string>();

            var mongoClientMock = new Mock<IMongoClient>();
            var databaseMock = new Mock<IMongoDatabase>();
            var collectionMock = new Mock<IMongoCollection<BsonDocument>>();
            var loggerMock = new Mock<ILogger<PushContactRepository>>();

            var settings = Options.Create(new PushMongoContextSettings
            {
                DatabaseName = "TestDatabase",
                PushContactsCollectionName = "TestCollection"
            });

            collectionMock
                .Setup(x => x.FindAsync(It.IsAny<FilterDefinition<BsonDocument>>(), It.IsAny<FindOptions<BsonDocument, BsonDocument>>(), default))
                .Throws(new Exception("Test exception"));

            databaseMock
                .Setup(x => x.GetCollection<BsonDocument>(settings.Value.PushContactsCollectionName, null))
                .Returns(collectionMock.Object);

            mongoClientMock
                .Setup(x => x.GetDatabase(settings.Value.DatabaseName, null))
                .Returns(databaseMock.Object);

            var sut = CreateSut(
                mongoClientMock.Object,
                settings,
                logger: loggerMock.Object);

            // Act
            var visitorInfo = await sut.GetVisitorInfoSafeAsync(deviceToken);

            // Assert
            Assert.Null(visitorInfo);
            loggerMock.Verify(
                x => x.Log(
                    It.Is<LogLevel>(l => l == LogLevel.Error),
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Unexpected error getting Visitor Info by")),
                    It.IsAny<Exception>(),
                    It.Is<Func<It.IsAnyType, Exception, string>>((v, t) => true)),
                Times.Once);
        }

        [Fact]
        public async Task GetDistinctVisitorGuidByDomain_should_log_error_and_throw_exception_when_push_contacts_fetching_fail()
        {
            // Arrange
            var fixture = new Fixture();

            var pushMongoContextSettings = fixture.Create<PushMongoContextSettings>();

            var domain = fixture.Create<string>();
            var page = fixture.Create<int>();
            var per_page = fixture.Create<int>();

            var expectedException = new Exception("Aggregate failed");

            var pushContactsCollectionMock = new Mock<IMongoCollection<BsonDocument>>();
            pushContactsCollectionMock.Setup(c => c.AggregateAsync(
                It.IsAny<PipelineDefinition<BsonDocument, BsonDocument>>(),
                It.IsAny<AggregateOptions>(),
                It.IsAny<CancellationToken>()))
                .ThrowsAsync(expectedException);

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
            await Assert.ThrowsAsync<Exception>(() => sut.GetDistinctVisitorGuidByDomain(domain, page, per_page));

            loggerMock.Verify(
                x => x.Log(
                    It.Is<LogLevel>(l => l == LogLevel.Error),
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Error getting distinct visitor_guids for domain:")),
                    It.Is<Exception>(ex => ex.Message == expectedException.Message),
                    It.Is<Func<It.IsAnyType, Exception, string>>((v, t) => true)),
                Times.Once);
        }

        [Fact]
        public async Task GetDistinctVisitorGuidByDomain_should_return_visitor_guids_filtered_by_domain()
        {
            // Arrange
            List<BsonDocument> allPushContactDocuments = FakePushContactDocuments(10);

            var random = new Random();
            int randomPushContactIndex = random.Next(allPushContactDocuments.Count);
            var domainFilter = allPushContactDocuments[randomPushContactIndex][PushContactDocumentProps.DomainPropName].AsString;

            var fixture = new Fixture();
            var page = fixture.Create<int>();
            var per_page = fixture.Create<int>();

            var pushMongoContextSettings = fixture.Create<PushMongoContextSettings>();

            var mockCursor = new Mock<IAsyncCursor<BsonDocument>>();
            mockCursor.Setup(_ => _.Current).Returns(allPushContactDocuments.Where(x => x[PushContactDocumentProps.DomainPropName].AsString == domainFilter));
            mockCursor
                .SetupSequence(_ => _.MoveNext(It.IsAny<CancellationToken>()))
                .Returns(true)
                .Returns(false);
            mockCursor
                .SetupSequence(_ => _.MoveNextAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(true)
                .ReturnsAsync(false);

            var pushContactsCollectionMock = new Mock<IMongoCollection<BsonDocument>>();
            pushContactsCollectionMock.Setup(c => c.AggregateAsync(
                It.IsAny<PipelineDefinition<BsonDocument, BsonDocument>>(),
                It.IsAny<AggregateOptions>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockCursor.Object);

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
            var result = await sut.GetDistinctVisitorGuidByDomain(domainFilter, page, per_page);

            // Assert
            Assert.All(
                result.Items,
                x => allPushContactDocuments
                    .Single(y => y[PushContactDocumentProps.DomainPropName].AsString == domainFilter && y[PushContactDocumentProps.VisitorGuidPropName].AsString == x)
            );
        }

        [Fact]
        public async Task GetAllVisitorGuidByDomain_should_throw_exception_and_log_error_when_push_contacts_cannot_be_getter_from_storage()
        {
            // Arrange
            var fixture = new Fixture();

            var pushMongoContextSettings = fixture.Create<PushMongoContextSettings>();

            var domain = fixture.Create<string>();

            var page = fixture.Create<int>();

            var per_page = fixture.Create<int>();

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
            await Assert.ThrowsAsync<Exception>(() => sut.GetAllVisitorGuidByDomain(domain, page, per_page));

            loggerMock.Verify(
                x => x.Log(
                    It.Is<LogLevel>(l => l == LogLevel.Error),
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString() == $"Error getting {nameof(PushContactModel)}s by {nameof(domain)} {domain}"),
                    It.IsAny<Exception>(),
                    It.Is<Func<It.IsAnyType, Exception, string>>((v, t) => true)),
                Times.Once);
        }

        [Fact]
        public async Task GetAllVisitorGuidByDomain_should_return_visitor_guids_filtered_by_domain()
        {
            // Arrange
            List<BsonDocument> allPushContactDocuments = FakePushContactDocuments(10);

            var random = new Random();
            int randomPushContactIndex = random.Next(allPushContactDocuments.Count);
            var domainFilter = allPushContactDocuments[randomPushContactIndex][PushContactDocumentProps.DomainPropName].AsString;

            var fixture = new Fixture();

            var page = fixture.Create<int>();

            var per_page = fixture.Create<int>();

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
            var result = await sut.GetAllVisitorGuidByDomain(domainFilter, page, per_page);

            // Assert
            Assert.All(result.Items, x => allPushContactDocuments
                                    .Single(y => y[PushContactDocumentProps.DomainPropName].AsString == domainFilter && y[PushContactDocumentProps.VisitorGuidPropName].AsString == x));
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
