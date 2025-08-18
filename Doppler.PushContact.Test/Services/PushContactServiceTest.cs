using AutoFixture;
using Doppler.PushContact.DTOs;
using Doppler.PushContact.Models;
using Doppler.PushContact.Models.DTOs;
using Doppler.PushContact.Models.Entities;
using Doppler.PushContact.Repositories.Interfaces;
using Doppler.PushContact.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Core.Clusters;
using MongoDB.Driver.Core.Connections;
using MongoDB.Driver.Core.Servers;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Doppler.PushContact.Test.Services
{
    public class PushContactServiceTest
    {
        private static PushContactService CreateSut(
            IMongoClient mongoClient = null,
            IOptions<PushMongoContextSettings> pushMongoContextSettings = null,
            IDeviceTokenValidator deviceTokenValidator = null,
            IPushContactRepository pushContactRepository = null,
            ILogger<PushContactService> logger = null)
        {
            return new PushContactService(
                mongoClient ?? Mock.Of<IMongoClient>(),
                pushMongoContextSettings ?? Mock.Of<IOptions<PushMongoContextSettings>>(),
                deviceTokenValidator ?? Mock.Of<IDeviceTokenValidator>(),
                pushContactRepository ?? Mock.Of<IPushContactRepository>(),
                logger ?? Mock.Of<ILogger<PushContactService>>());
        }

        [Fact]
        public async Task AddAsync_should_throw_argument_null_exception_when_push_contact_model_is_null()
        {
            // Arrange
            PushContactModel pushContactModel = null;

            var sut = CreateSut();

            // Act
            // Assert
            var result = await Assert.ThrowsAsync<ArgumentNullException>(() => sut.AddAsync(pushContactModel));
        }

        [Fact]
        public async Task AddAsync_should_throw_argument_exception_when_device_token_is_not_valid()
        {
            // Arrange
            var fixture = new Fixture();

            var pushContactModel = fixture.Create<PushContactModel>();

            var deviceTokenValidator = new Mock<IDeviceTokenValidator>();
            deviceTokenValidator
                .Setup(x => x.IsValidAsync(pushContactModel.DeviceToken))
                .ReturnsAsync(false);

            var sut = CreateSut(deviceTokenValidator: deviceTokenValidator.Object);

            // Act
            // Assert
            await Assert.ThrowsAsync<ArgumentException>(() => sut.AddAsync(pushContactModel));
        }

        [Fact]
        public async Task AddAsync_should_throw_exception_and_log_error_when_a_push_contact_model_cannot_be_added()
        {
            // Arrange
            var fixture = new Fixture();

            var pushContactModel = fixture.Create<PushContactModel>();

            var deviceTokenValidator = new Mock<IDeviceTokenValidator>();
            deviceTokenValidator
                .Setup(x => x.IsValidAsync(pushContactModel.DeviceToken))
                .ReturnsAsync(true);

            var pushMongoContextSettings = fixture.Create<PushMongoContextSettings>();

            var pushContactsCollectionMock = new Mock<IMongoCollection<BsonDocument>>();
            pushContactsCollectionMock
                .Setup(x => x.InsertOneAsync(It.IsAny<BsonDocument>(), null, default))
                .ThrowsAsync(new Exception());

            var mongoDatabaseMock = new Mock<IMongoDatabase>();
            mongoDatabaseMock
                .Setup(x => x.GetCollection<BsonDocument>(pushMongoContextSettings.PushContactsCollectionName, null))
                .Returns(pushContactsCollectionMock.Object);

            var mongoClientMock = new Mock<IMongoClient>();
            mongoClientMock
                .Setup(x => x.GetDatabase(pushMongoContextSettings.DatabaseName, null))
                .Returns(mongoDatabaseMock.Object);

            var loggerMock = new Mock<ILogger<PushContactService>>();

            var sut = CreateSut(
                mongoClientMock.Object,
                Options.Create(pushMongoContextSettings),
                deviceTokenValidator.Object,
                null,
                loggerMock.Object);

            // Act
            // Assert
            await Assert.ThrowsAsync<Exception>(() => sut.AddAsync(pushContactModel));

            loggerMock.Verify(
                x => x.Log(
                    It.Is<LogLevel>(l => l == LogLevel.Error),
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString() == @$"Error inserting {nameof(pushContactModel)}
with following {nameof(pushContactModel.DeviceToken)}: {pushContactModel.DeviceToken}"),
                    It.IsAny<Exception>(),
                    It.Is<Func<It.IsAnyType, Exception, string>>((v, t) => true)),
                Times.Once);
        }

        [Fact]
        public async Task AddAsync_should_not_throw_exception_when_a_push_contact_model_can_be_added()
        {
            // Arrange
            var fixture = new Fixture();

            var pushContactModel = fixture.Create<PushContactModel>();

            var deviceTokenValidator = new Mock<IDeviceTokenValidator>();
            deviceTokenValidator
                .Setup(x => x.IsValidAsync(pushContactModel.DeviceToken))
                .ReturnsAsync(true);

            var pushMongoContextSettings = fixture.Create<PushMongoContextSettings>();

            var pushContactsCollection = new Mock<IMongoCollection<BsonDocument>>();
            pushContactsCollection
                .Setup(x => x.InsertOneAsync(It.IsAny<BsonDocument>(), null, default))
                .Returns(Task.CompletedTask);

            var mongoDatabase = new Mock<IMongoDatabase>();
            mongoDatabase
                .Setup(x => x.GetCollection<BsonDocument>(pushMongoContextSettings.PushContactsCollectionName, null))
                .Returns(pushContactsCollection.Object);

            var mongoClient = new Mock<IMongoClient>();
            mongoClient
                .Setup(x => x.GetDatabase(pushMongoContextSettings.DatabaseName, null))
                .Returns(mongoDatabase.Object);

            var sut = CreateSut(
                mongoClient.Object,
                Options.Create(pushMongoContextSettings),
                deviceTokenValidator.Object
            );

            // Act
            // Assert
            await sut.AddAsync(pushContactModel);
        }

        [Fact]
        public async Task AddAsync_should_treat_as_successful_when_a_push_contact_was_not_added_due_to_a_duplicated_deviceToken()
        {
            // Arrange
            var fixture = new Fixture();
            var pushContactModel = fixture.Create<PushContactModel>();

            var deviceTokenValidator = new Mock<IDeviceTokenValidator>();
            deviceTokenValidator
                .Setup(x => x.IsValidAsync(pushContactModel.DeviceToken))
                .ReturnsAsync(true);

            var pushMongoContextSettings = fixture.Create<PushMongoContextSettings>();

            var mongoWriteException = CreateMongoWriteException();

            var pushContactsCollectionMock = new Mock<IMongoCollection<BsonDocument>>();
            pushContactsCollectionMock
                .Setup(x => x.InsertOneAsync(It.IsAny<BsonDocument>(), null, default))
                .ThrowsAsync(mongoWriteException);

            var mongoDatabaseMock = new Mock<IMongoDatabase>();
            mongoDatabaseMock
                .Setup(x => x.GetCollection<BsonDocument>(pushMongoContextSettings.PushContactsCollectionName, null))
                .Returns(pushContactsCollectionMock.Object);

            var mongoClientMock = new Mock<IMongoClient>();
            mongoClientMock
                .Setup(x => x.GetDatabase(pushMongoContextSettings.DatabaseName, null))
                .Returns(mongoDatabaseMock.Object);

            var loggerMock = new Mock<ILogger<PushContactService>>();

            var sut = CreateSut(
                mongoClientMock.Object,
                Options.Create(pushMongoContextSettings),
                deviceTokenValidator.Object,
                null,
                loggerMock.Object
            );

            // Act
            await sut.AddAsync(pushContactModel);

            // Assert
            loggerMock.Verify(
                x => x.Log(
                    It.Is<LogLevel>(l => l == LogLevel.Information),
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Duplicate key error for")),
                    It.IsAny<Exception>(),
                    It.Is<Func<It.IsAnyType, Exception, string>>((v, t) => true)),
                Times.Once);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public async Task AddAsync_should_allow_null_or_empty_email(string email)
        {
            // Arrange
            var fixture = new Fixture();

            var pushContactModel = new PushContactModel
            {
                Domain = fixture.Create<string>(),
                DeviceToken = fixture.Create<string>(),
                Email = email
            };

            var deviceTokenValidator = new Mock<IDeviceTokenValidator>();
            deviceTokenValidator
                .Setup(x => x.IsValidAsync(pushContactModel.DeviceToken))
                .ReturnsAsync(true);

            var pushMongoContextSettings = fixture.Create<PushMongoContextSettings>();

            var pushContactsCollection = new Mock<IMongoCollection<BsonDocument>>();
            pushContactsCollection
                .Setup(x => x.InsertOneAsync(It.IsAny<BsonDocument>(), null, default))
                .Returns(Task.CompletedTask);

            var mongoDatabase = new Mock<IMongoDatabase>();
            mongoDatabase
                .Setup(x => x.GetCollection<BsonDocument>(pushMongoContextSettings.PushContactsCollectionName, null))
                .Returns(pushContactsCollection.Object);

            var mongoClient = new Mock<IMongoClient>();
            mongoClient
                .Setup(x => x.GetDatabase(pushMongoContextSettings.DatabaseName, null))
                .Returns(mongoDatabase.Object);

            var sut = CreateSut(
                mongoClient.Object,
                Options.Create(pushMongoContextSettings),
                deviceTokenValidator.Object);

            // Act
            // Assert
            await sut.AddAsync(pushContactModel);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public async Task AddAsync_should_throw_argument_exception_when_device_token_is_null_or_empty(string deviceToken)
        {
            // Arrange
            var fixture = new Fixture();

            var pushContactModel = new PushContactModel
            {
                Domain = fixture.Create<string>(),
                DeviceToken = deviceToken,
                Email = fixture.Create<string>()
            };

            var deviceTokenValidator = new Mock<IDeviceTokenValidator>();
            deviceTokenValidator
                .Setup(x => x.IsValidAsync(pushContactModel.DeviceToken))
                .ReturnsAsync(true);

            var pushMongoContextSettings = fixture.Create<PushMongoContextSettings>();

            var pushContactsCollection = new Mock<IMongoCollection<BsonDocument>>();
            pushContactsCollection
                .Setup(x => x.InsertOneAsync(It.IsAny<BsonDocument>(), null, default))
                .Returns(Task.CompletedTask);

            var mongoDatabase = new Mock<IMongoDatabase>();
            mongoDatabase
                .Setup(x => x.GetCollection<BsonDocument>(pushMongoContextSettings.PushContactsCollectionName, null))
                .Returns(pushContactsCollection.Object);

            var mongoClient = new Mock<IMongoClient>();
            mongoClient
                .Setup(x => x.GetDatabase(pushMongoContextSettings.DatabaseName, null))
                .Returns(mongoDatabase.Object);

            var sut = CreateSut(
                mongoClient.Object,
                Options.Create(pushMongoContextSettings),
                deviceTokenValidator.Object
            );

            // Act
            // Assert
            await Assert.ThrowsAsync<ArgumentException>(() => sut.AddAsync(pushContactModel));
        }

        [Theory]
        [InlineData("invalidFirebaseToken", false, "invalidEndpoint", "validAuthValue", "validP256dhValue")]
        [InlineData("invalidFirebaseToken", false, null, "validAuthValue", "validP256dhValue")]
        [InlineData("invalidFirebaseToken", false, "https://valid.endpoint.com", "", "validP256dhValue")]
        [InlineData("invalidFirebaseToken", false, "https://valid.endpoint.com", null, "validP256dhValue")]
        [InlineData("invalidFirebaseToken", false, "https://valid.endpoint.com", "validAuthValue", "")]
        [InlineData("invalidFirebaseToken", false, "https://valid.endpoint.com", "validAuthValue", null)]
        public async Task AddAsync_should_throw_argument_exception_when_deviceToken_and_subscription_are_notValid(
            string deviceToken,
            bool isAValidFirebaseToken,
            string endpoint,
            string auth,
            string p256dh
        )
        {
            // Arrange
            var fixture = new Fixture();

            var pushContactModel = new PushContactModel
            {
                Domain = fixture.Create<string>(),
                DeviceToken = deviceToken,
                Email = fixture.Create<string>(),
                Subscription = new SubscriptionDTO()
                {
                    EndPoint = endpoint,
                    Keys = new SubscriptionKeys()
                    {
                        Auth = auth,
                        P256DH = p256dh,
                    }
                }
            };

            var deviceTokenValidator = new Mock<IDeviceTokenValidator>();
            deviceTokenValidator
                .Setup(x => x.IsValidAsync(pushContactModel.DeviceToken))
                .ReturnsAsync(isAValidFirebaseToken);

            var pushMongoContextSettings = fixture.Create<PushMongoContextSettings>();

            var pushContactsCollection = new Mock<IMongoCollection<BsonDocument>>();

            var sut = CreateSut(
                deviceTokenValidator: deviceTokenValidator.Object
            );

            // Act
            // Assert
            await Assert.ThrowsAsync<ArgumentException>(() => sut.AddAsync(pushContactModel));
        }

        [Theory]
        [InlineData("invalidValidFirebaseToken", false, "https://valid.endpoint.com", "authValue", "p256dhValue")]
        [InlineData("validFirebaseToken", true, "invalidEndpoint", "authValue", "p256dhValue")]
        public async Task AddAsync_should_finish_ok_when_subscription_isValid_or_if_deviceToken_isValid_for_Firebase(
            string deviceToken,
            bool isAValidFirebaseToken,
            string endpoint,
            string auth,
            string p256dh
        )
        {
            // Arrange
            var fixture = new Fixture();

            var pushContactModel = new PushContactModel
            {
                Domain = fixture.Create<string>(),
                DeviceToken = deviceToken,
                Email = fixture.Create<string>(),
                Subscription = new SubscriptionDTO
                {
                    EndPoint = endpoint,
                    Keys = new SubscriptionKeys
                    {
                        Auth = auth,
                        P256DH = p256dh
                    }
                }
            };

            var deviceTokenValidator = new Mock<IDeviceTokenValidator>();
            deviceTokenValidator
                .Setup(x => x.IsValidAsync(pushContactModel.DeviceToken))
                .ReturnsAsync(isAValidFirebaseToken);

            var pushMongoContextSettings = fixture.Create<PushMongoContextSettings>();

            var pushContactsCollection = new Mock<IMongoCollection<BsonDocument>>();
            pushContactsCollection
                .Setup(x => x.InsertOneAsync(It.IsAny<BsonDocument>(), null, default))
                .Returns(Task.CompletedTask);

            var mongoDatabase = new Mock<IMongoDatabase>();
            mongoDatabase
                .Setup(x => x.GetCollection<BsonDocument>(pushMongoContextSettings.PushContactsCollectionName, null))
                .Returns(pushContactsCollection.Object);

            var mongoClient = new Mock<IMongoClient>();
            mongoClient
                .Setup(x => x.GetDatabase(pushMongoContextSettings.DatabaseName, null))
                .Returns(mongoDatabase.Object);

            var loggerMock = new Mock<ILogger<PushContactService>>();

            var sut = CreateSut(
                mongoClient.Object,
                Options.Create(pushMongoContextSettings),
                deviceTokenValidator.Object,
                null,
                loggerMock.Object
            );

            // Act
            await sut.AddAsync(pushContactModel);

            // Assert
            pushContactsCollection.Verify(
                x => x.InsertOneAsync(It.IsAny<BsonDocument>(), null, default),
                Times.Once
            );
        }

        [Fact]
        public async Task GetAsync_should_throw_argument_null_exception_when_push_contact_filter_is_null()
        {
            // Arrange
            PushContactFilter pushContactFilter = null;

            var sut = CreateSut();

            // Act
            // Assert
            var result = await Assert.ThrowsAsync<ArgumentNullException>(() => sut.GetAsync(pushContactFilter));
        }

        [Theory]
        [InlineData("someDeviceToken", null)]
        [InlineData(null, "someEmail")]
        [InlineData(null, null)]
        public async Task UpdateEmailAsync_should_throw_argument_null_exception_when_device_token_or_email_are_null(string deviceToken, string email)
        {
            // Arrange
            var sut = CreateSut();

            // Act
            // Assert
            var result = await Assert.ThrowsAsync<ArgumentNullException>(() => sut.UpdateEmailAsync(deviceToken, email));
        }

        [Fact]
        public async Task UpdateEmailAsync_should_throw_exception_and_log_error_when_push_contact_model_cannot_be_updated()
        {
            // Arrange
            var fixture = new Fixture();

            var deviceToken = fixture.Create<string>();
            var email = fixture.Create<string>();

            var pushMongoContextSettings = fixture.Create<PushMongoContextSettings>();

            var pushContactsCollectionMock = new Mock<IMongoCollection<BsonDocument>>();
            pushContactsCollectionMock
                .Setup(x => x.UpdateOneAsync(It.IsAny<FilterDefinition<BsonDocument>>(), It.IsAny<UpdateDefinition<BsonDocument>>(), default, default))
                .ThrowsAsync(new Exception());

            var mongoDatabaseMock = new Mock<IMongoDatabase>();
            mongoDatabaseMock
                .Setup(x => x.GetCollection<BsonDocument>(pushMongoContextSettings.PushContactsCollectionName, null))
                .Returns(pushContactsCollectionMock.Object);

            var mongoClientMock = new Mock<IMongoClient>();
            mongoClientMock
                .Setup(x => x.GetDatabase(pushMongoContextSettings.DatabaseName, null))
                .Returns(mongoDatabaseMock.Object);

            var loggerMock = new Mock<ILogger<PushContactService>>();

            var sut = CreateSut(
                mongoClientMock.Object,
                Options.Create(pushMongoContextSettings),
                logger: loggerMock.Object);

            // Act
            // Assert
            await Assert.ThrowsAsync<Exception>(() => sut.UpdateEmailAsync(deviceToken, email));
            loggerMock.Verify(
                x => x.Log(
                    It.Is<LogLevel>(l => l == LogLevel.Error),
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString() == @$"Error updating {nameof(PushContactModel)}
with {nameof(deviceToken)} {deviceToken}. {PushContactDocumentProps.EmailPropName} can not be updated with following value: {email}"),
                    It.IsAny<Exception>(),
                    It.Is<Func<It.IsAnyType, Exception, string>>((v, t) => true)),
                Times.Once);
        }

        [Fact]
        public async Task UpdateEmailAsync_should_not_throw_exception_when_push_contact_model_can_be_updated()
        {
            // Arrange
            var fixture = new Fixture();

            var deviceToken = fixture.Create<string>();
            var email = fixture.Create<string>();

            var updateResultMock = new Mock<UpdateResult>();

            var pushMongoContextSettings = fixture.Create<PushMongoContextSettings>();

            var pushContactsCollection = new Mock<IMongoCollection<BsonDocument>>();
            pushContactsCollection
                .Setup(x => x.UpdateOneAsync(It.IsAny<FilterDefinition<BsonDocument>>(), It.IsAny<UpdateDefinition<BsonDocument>>(), default, default))
                .ReturnsAsync(updateResultMock.Object);

            var mongoDatabase = new Mock<IMongoDatabase>();
            mongoDatabase
                .Setup(x => x.GetCollection<BsonDocument>(pushMongoContextSettings.PushContactsCollectionName, null))
                .Returns(pushContactsCollection.Object);

            var mongoClient = new Mock<IMongoClient>();
            mongoClient
                .Setup(x => x.GetDatabase(pushMongoContextSettings.DatabaseName, null))
                .Returns(mongoDatabase.Object);

            var sut = CreateSut(
                mongoClient.Object,
                Options.Create(pushMongoContextSettings));

            // Act
            // Assert
            await sut.UpdateEmailAsync(deviceToken, email);
        }

        [Fact]
        public async Task UpdatePushContactVisitorGuid_should_not_throw_exception_when_push_contact_model_can_be_updated()
        {
            // Arrange
            var fixture = new Fixture();

            var deviceToken = fixture.Create<string>();
            var visitorGuid = fixture.Create<string>();

            var updateResultMock = new Mock<UpdateResult>();

            var pushMongoContextSettings = fixture.Create<PushMongoContextSettings>();

            var pushContactsCollection = new Mock<IMongoCollection<BsonDocument>>();
            pushContactsCollection
                .Setup(x => x.UpdateOneAsync(It.IsAny<FilterDefinition<BsonDocument>>(), It.IsAny<UpdateDefinition<BsonDocument>>(), default, default))
                .ReturnsAsync(updateResultMock.Object);

            var mongoDatabase = new Mock<IMongoDatabase>();
            mongoDatabase
                .Setup(x => x.GetCollection<BsonDocument>(pushMongoContextSettings.PushContactsCollectionName, null))
                .Returns(pushContactsCollection.Object);

            var mongoClient = new Mock<IMongoClient>();
            mongoClient
                .Setup(x => x.GetDatabase(pushMongoContextSettings.DatabaseName, null))
                .Returns(mongoDatabase.Object);

            var sut = CreateSut(
                mongoClient.Object,
                Options.Create(pushMongoContextSettings));

            // Act
            // Assert
            await sut.UpdatePushContactVisitorGuid(deviceToken, visitorGuid);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public async Task GetAsync_should_throw_argument_exception_when_push_contact_filter_domain_is_null_or_empty
            (string domain)
        {
            // Arrange
            var pushContactFilter = new PushContactFilter(domain);

            var sut = CreateSut();

            // Act
            // Assert
            var result = await Assert.ThrowsAsync<ArgumentException>(() => sut.GetAsync(pushContactFilter));
        }

        [Fact]
        public async Task
            GetAsync_should_throw_argument_exception_when_push_contact_filter_modified_from_is_greater_than_modified_to()
        {
            // Arrange
            var fixture = new Fixture();

            var domain = fixture.Create<string>();
            var modifiedFrom = fixture.Create<DateTime>();
            var modifiedTo = modifiedFrom.AddDays(-1);

            var pushContactFilter = new PushContactFilter(domain: domain, modifiedFrom: modifiedFrom, modifiedTo: modifiedTo);

            var sut = CreateSut();

            // Act
            // Assert
            var result = await Assert.ThrowsAsync<ArgumentException>(() => sut.GetAsync(pushContactFilter));
            Assert.Equal($"'{nameof(pushContactFilter.ModifiedFrom)}' cannot be greater than '{nameof(pushContactFilter.ModifiedTo)}'", result.Message);
        }

        [Fact]
        public async Task GetAsync_should_throw_exception_and_log_error_when_push_contacts_cannot_be_getter()
        {
            // Arrange
            var fixture = new Fixture();

            var pushMongoContextSettings = fixture.Create<PushMongoContextSettings>();

            var domain = fixture.Create<string>();
            var pushContactFilter = new PushContactFilter(domain);

            var pushContactsCollectionMock = new Mock<IMongoCollection<BsonDocument>>();
            pushContactsCollectionMock
                .Setup(x => x.FindAsync<BsonDocument>(It.IsAny<FilterDefinition<BsonDocument>>(), null, default))
                .ThrowsAsync(new Exception());

            var mongoDatabaseMock = new Mock<IMongoDatabase>();
            mongoDatabaseMock
                .Setup(x => x.GetCollection<BsonDocument>(pushMongoContextSettings.PushContactsCollectionName, null))
                .Returns(pushContactsCollectionMock.Object);

            var mongoClientMock = new Mock<IMongoClient>();
            mongoClientMock
                .Setup(x => x.GetDatabase(pushMongoContextSettings.DatabaseName, null))
                .Returns(mongoDatabaseMock.Object);

            var loggerMock = new Mock<ILogger<PushContactService>>();

            var sut = CreateSut(
                mongoClientMock.Object,
                Options.Create(pushMongoContextSettings),
                logger: loggerMock.Object);

            // Act
            // Assert
            await Assert.ThrowsAsync<Exception>(() => sut.GetAsync(pushContactFilter));

            loggerMock.Verify(
                x => x.Log(
                    It.Is<LogLevel>(l => l == LogLevel.Error),
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString() == $"Error getting {nameof(PushContactModel)}s"),
                    It.IsAny<Exception>(),
                    It.Is<Func<It.IsAnyType, Exception, string>>((v, t) => true)),
                Times.Once);
        }

        [Fact]
        public async Task GetAsync_should_return_push_contacts_filtered_by_domain()
        {
            // Arrange
            List<BsonDocument> allPushContactDocuments = FakePushContactDocuments(10);

            var random = new Random();
            int randomPushContactIndex = random.Next(allPushContactDocuments.Count);
            var domainFilter = allPushContactDocuments[randomPushContactIndex][PushContactDocumentProps.DomainPropName].AsString;
            var pushContactFilter = new PushContactFilter(domainFilter);

            var fixture = new Fixture();

            var pushMongoContextSettings = fixture.Create<PushMongoContextSettings>();

            var pushContactsCursorMock = new Mock<IAsyncCursor<BsonDocument>>();
            pushContactsCursorMock
                .Setup(_ => _.Current)
                .Returns(allPushContactDocuments.Where(x => x[PushContactDocumentProps.DomainPropName].AsString == pushContactFilter.Domain));

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
                .Setup(x => x.FindAsync<BsonDocument>(It.IsAny<FilterDefinition<BsonDocument>>(), null, default))
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
            var result = await sut.GetAsync(pushContactFilter);

            // Assert
            Assert.True(result.Any());
            Assert.True(result.All(x => x.Domain == pushContactFilter.Domain));
        }

        [Fact]
        public async Task GetAsync_should_return_push_contacts_filtered_by_domain_and_email()
        {
            // Arrange
            List<BsonDocument> allPushContactDocuments = FakePushContactDocuments(10);

            var random = new Random();
            int randomPushContactIndex = random.Next(allPushContactDocuments.Count);
            var domainFilter = allPushContactDocuments[randomPushContactIndex][PushContactDocumentProps.DomainPropName].AsString;
            var emailFilter = allPushContactDocuments[randomPushContactIndex][PushContactDocumentProps.EmailPropName].AsString;
            var pushContactFilter = new PushContactFilter(domainFilter, emailFilter);

            var fixture = new Fixture();

            var pushMongoContextSettings = fixture.Create<PushMongoContextSettings>();

            var pushContactsCursorMock = new Mock<IAsyncCursor<BsonDocument>>();
            pushContactsCursorMock
                .Setup(_ => _.Current)
                .Returns(allPushContactDocuments.Where(x => x[PushContactDocumentProps.DomainPropName].AsString == pushContactFilter.Domain && x[PushContactDocumentProps.EmailPropName].AsString == pushContactFilter.Email));

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
                .Setup(x => x.FindAsync<BsonDocument>(It.IsAny<FilterDefinition<BsonDocument>>(), null, default))
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
            var result = await sut.GetAsync(pushContactFilter);

            // Assert
            Assert.True(result.Any());
            Assert.True(result.All(x => x.Domain == pushContactFilter.Domain && x.Email == pushContactFilter.Email));
        }

        [Fact]
        public async Task GetAsync_should_return_not_deleted_push_contacts()
        {
            // Arrange
            List<BsonDocument> allPushContactDocuments = FakePushContactDocuments(10);
            var random = new Random();
            int randomPushContactIndex = random.Next(allPushContactDocuments.Count);
            allPushContactDocuments[randomPushContactIndex][PushContactDocumentProps.DeletedPropName] = false;

            var fixture = new Fixture();

            var pushMongoContextSettings = fixture.Create<PushMongoContextSettings>();

            var pushContactsCursorMock = new Mock<IAsyncCursor<BsonDocument>>();
            pushContactsCursorMock
                .Setup(_ => _.Current)
                .Returns(allPushContactDocuments.Where(x => x[PushContactDocumentProps.DeletedPropName].AsBoolean == false));

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
                .Setup(x => x.FindAsync<BsonDocument>(It.IsAny<FilterDefinition<BsonDocument>>(), null, default))
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

            var domain = fixture.Create<string>();
            var pushContactFilter = new PushContactFilter(domain);

            // Act
            var result = await sut.GetAsync(pushContactFilter);

            // Assert
            Assert.True(result.Any());
            Assert.True(result.All(x => allPushContactDocuments.Exists(y => y[PushContactDocumentProps.DomainPropName] == x.Domain
                                                                            && y[PushContactDocumentProps.DeviceTokenPropName] == x.DeviceToken
                                                                            && y[PushContactDocumentProps.EmailPropName] == x.Email
                                                                            && y[PushContactDocumentProps.DeletedPropName].AsBoolean == false)));
        }

        [Fact]
        public async Task GetAsync_should_not_throw_exceptions_when_push_contacts_documents_have_not_some_property()
        {
            // Arrange
            List<BsonDocument> pushContactDocumentsWithoutProperties = Enumerable.Repeat(0, 10)
                .Select(x =>
                {
                    return new BsonDocument { };
                })
                .ToList();

            var fixture = new Fixture();

            var pushMongoContextSettings = fixture.Create<PushMongoContextSettings>();

            var pushContactsCursorMock = new Mock<IAsyncCursor<BsonDocument>>();
            pushContactsCursorMock
                .Setup(_ => _.Current)
                .Returns(pushContactDocumentsWithoutProperties);

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
                .Setup(x => x.FindAsync<BsonDocument>(It.IsAny<FilterDefinition<BsonDocument>>(), null, default))
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

            var domain = fixture.Create<string>();
            var pushContactFilter = new PushContactFilter(domain);

            // Act
            var result = await sut.GetAsync(pushContactFilter);

            // Assert
            _ = result.ToList();
        }

        [Fact]
        public async Task DeleteByDeviceTokenAsync_should_throw_argument_exception_when_device_tokens_collection_is_null()
        {
            // Arrange
            IEnumerable<string> deviceTokens = null;

            var sut = CreateSut();

            // Act
            // Assert
            var result = await Assert.ThrowsAsync<ArgumentException>(() => sut.DeleteByDeviceTokenAsync(deviceTokens));
        }

        [Fact]
        public async Task DeleteByDeviceTokenAsync_should_throw_argument_exception_when_device_tokens_collection_is_empty()
        {
            // Arrange
            IEnumerable<string> deviceTokens = new List<string>();

            var sut = CreateSut();

            // Act
            // Assert
            var result = await Assert.ThrowsAsync<ArgumentException>(() => sut.DeleteByDeviceTokenAsync(deviceTokens));
        }

        [Fact]
        public async Task DeleteByDeviceTokenAsync_should_throw_exception_and_log_error_when_push_contact_models_cannot_be_deleted()
        {
            // Arrange
            var fixture = new Fixture();

            var deviceTokens = fixture.CreateMany<string>();

            var pushMongoContextSettings = fixture.Create<PushMongoContextSettings>();

            var pushContactsCollectionMock = new Mock<IMongoCollection<BsonDocument>>();
            pushContactsCollectionMock
                .Setup(x => x.UpdateManyAsync(It.IsAny<FilterDefinition<BsonDocument>>(), It.IsAny<UpdateDefinition<BsonDocument>>(), default, default))
                .ThrowsAsync(new Exception());

            var mongoDatabaseMock = new Mock<IMongoDatabase>();
            mongoDatabaseMock
                .Setup(x => x.GetCollection<BsonDocument>(pushMongoContextSettings.PushContactsCollectionName, null))
                .Returns(pushContactsCollectionMock.Object);

            var mongoClientMock = new Mock<IMongoClient>();
            mongoClientMock
                .Setup(x => x.GetDatabase(pushMongoContextSettings.DatabaseName, null))
                .Returns(mongoDatabaseMock.Object);

            var loggerMock = new Mock<ILogger<PushContactService>>();

            var sut = CreateSut(
                mongoClientMock.Object,
                Options.Create(pushMongoContextSettings),
                logger: loggerMock.Object);

            // Act
            // Assert
            await Assert.ThrowsAsync<Exception>(() => sut.DeleteByDeviceTokenAsync(deviceTokens));
            loggerMock.Verify(
                x => x.Log(
                    It.Is<LogLevel>(l => l == LogLevel.Error),
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString() == $"Error deleting {nameof(PushContactModel)}s"),
                    It.IsAny<Exception>(),
                    It.Is<Func<It.IsAnyType, Exception, string>>((v, t) => true)),
                Times.Once);
        }

        [Fact]
        public async Task DeleteByDeviceTokenAsync_should_return_deleted_count_when_push_contact_models_can_be_deleted()
        {
            // Arrange
            var fixture = new Fixture();

            var deviceTokens = fixture.CreateMany<string>();
            var expectedDeletedCount = fixture.Create<long>();

            var updateResultMock = new Mock<UpdateResult>();
            updateResultMock.Setup(_ => _.IsModifiedCountAvailable).Returns(true);
            updateResultMock.Setup(_ => _.ModifiedCount).Returns(expectedDeletedCount);

            var pushMongoContextSettings = fixture.Create<PushMongoContextSettings>();

            var pushContactsCollection = new Mock<IMongoCollection<BsonDocument>>();
            pushContactsCollection
                .Setup(x => x.UpdateManyAsync(It.IsAny<FilterDefinition<BsonDocument>>(), It.IsAny<UpdateDefinition<BsonDocument>>(), default, default))
                .ReturnsAsync(updateResultMock.Object);

            var mongoDatabase = new Mock<IMongoDatabase>();
            mongoDatabase
                .Setup(x => x.GetCollection<BsonDocument>(pushMongoContextSettings.PushContactsCollectionName, null))
                .Returns(pushContactsCollection.Object);

            var mongoClient = new Mock<IMongoClient>();
            mongoClient
                .Setup(x => x.GetDatabase(pushMongoContextSettings.DatabaseName, null))
                .Returns(mongoDatabase.Object);

            var sut = CreateSut(
                mongoClient.Object,
                Options.Create(pushMongoContextSettings));

            // Act
            var deletedCount = await sut.DeleteByDeviceTokenAsync(deviceTokens);

            // Assert
            Assert.Equal(expectedDeletedCount, updateResultMock.Object.ModifiedCount);
        }

        [Fact]
        public async Task AddHistoryEventsAsync_should_throw_argument_exception_when_push_contact_history_events_collection_is_null()
        {
            // Arrange
            IEnumerable<PushContactHistoryEvent> pushContactHistoryEvents = null;

            var sut = CreateSut();

            // Act
            // Assert
            var result = await Assert.ThrowsAsync<ArgumentException>(() => sut.AddHistoryEventsAsync(pushContactHistoryEvents));
        }

        [Fact]
        public async Task AddHistoryEventsAsync_should_throw_argument_exception_when_push_contact_history_events_collection_is_empty()
        {
            // Arrange
            IEnumerable<PushContactHistoryEvent> pushContactHistoryEvents = new List<PushContactHistoryEvent>();

            var sut = CreateSut();

            // Act
            // Assert
            var result = await Assert.ThrowsAsync<ArgumentException>(() => sut.AddHistoryEventsAsync(pushContactHistoryEvents));
        }

        [Fact]
        public async Task AddHistoryEventsAsync_should_throw_exception_and_log_error_when_push_contact_history_events_cannot_be_added()
        {
            // Arrange
            var fixture = new Fixture();

            var pushContactHistoryEvents = fixture.CreateMany<PushContactHistoryEvent>();

            var pushMongoContextSettings = fixture.Create<PushMongoContextSettings>();

            var pushContactsCollectionMock = new Mock<IMongoCollection<BsonDocument>>();
            pushContactsCollectionMock
                .Setup(x => x.BulkWriteAsync(It.IsAny<IEnumerable<UpdateOneModel<BsonDocument>>>(), default, default))
                .ThrowsAsync(new Exception());

            var mongoDatabaseMock = new Mock<IMongoDatabase>();
            mongoDatabaseMock
                .Setup(x => x.GetCollection<BsonDocument>(pushMongoContextSettings.PushContactsCollectionName, null))
                .Returns(pushContactsCollectionMock.Object);

            var mongoClientMock = new Mock<IMongoClient>();
            mongoClientMock
                .Setup(x => x.GetDatabase(pushMongoContextSettings.DatabaseName, null))
                .Returns(mongoDatabaseMock.Object);

            var loggerMock = new Mock<ILogger<PushContactService>>();

            var sut = CreateSut(
                mongoClientMock.Object,
                Options.Create(pushMongoContextSettings),
                logger: loggerMock.Object);

            // Act
            // Assert
            await Assert.ThrowsAsync<Exception>(() => sut.AddHistoryEventsAsync(pushContactHistoryEvents));
            loggerMock.Verify(
                x => x.Log(
                    It.Is<LogLevel>(l => l == LogLevel.Error),
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString() == $"Error adding {nameof(PushContactHistoryEvent)}s"),
                    It.IsAny<Exception>(),
                    It.Is<Func<It.IsAnyType, Exception, string>>((v, t) => true)),
                Times.Once);
        }

        [Fact]
        public async Task AddHistoryEventsAsync_should_not_throw_exception_and_not_log_error_when_push_contact_history_events_can_be_added()
        {
            // Arrange
            var fixture = new Fixture();

            var pushContactHistoryEvents = fixture.CreateMany<PushContactHistoryEvent>();

            var pushMongoContextSettings = fixture.Create<PushMongoContextSettings>();

            var pushContactsCollection = new Mock<IMongoCollection<BsonDocument>>();
            pushContactsCollection
                .Setup(x => x.BulkWriteAsync(It.IsAny<IEnumerable<UpdateOneModel<BsonDocument>>>(), default, default))
                .Returns(Task.FromResult(It.IsAny<BulkWriteResult<BsonDocument>>()));

            var mongoDatabase = new Mock<IMongoDatabase>();
            mongoDatabase
                .Setup(x => x.GetCollection<BsonDocument>(pushMongoContextSettings.PushContactsCollectionName, null))
                .Returns(pushContactsCollection.Object);

            var mongoClient = new Mock<IMongoClient>();
            mongoClient
                .Setup(x => x.GetDatabase(pushMongoContextSettings.DatabaseName, null))
                .Returns(mongoDatabase.Object);

            var loggerMock = new Mock<ILogger<PushContactService>>();

            var sut = CreateSut(
                mongoClient.Object,
                Options.Create(pushMongoContextSettings),
                logger: loggerMock.Object);

            // Act
            await sut.AddHistoryEventsAsync(pushContactHistoryEvents);

            // Assert
            loggerMock.Verify(
                x => x.Log(
                    It.Is<LogLevel>(l => l == LogLevel.Error),
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString() == $"Error adding {nameof(PushContactHistoryEvent)}s"),
                    It.IsAny<Exception>(),
                    It.Is<Func<It.IsAnyType, Exception, string>>((v, t) => true)),
                Times.Never);
        }

        [Fact]
        public async Task UpdatePushContactsAsync_should_throw_argument_exception_when_SendMessageResult_is_null()
        {
            // Arrange
            var messageId = new Guid("ccf7ad9b-bd9a-465a-b240-602c93141bf3");
            var sut = CreateSut();
            // Act
            // Assert
            var result = await Assert.ThrowsAsync<ArgumentNullException>(() => sut.AddHistoryEventsAsync(messageId, null));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public async Task GetAllDeviceTokensByDomainAsync_should_throw_argument_exception_when_domain_is_null_or_empty
            (string domain)
        {
            // Arrange
            var sut = CreateSut();

            // Act
            // Assert
            var result = await Assert.ThrowsAsync<ArgumentException>(() => sut.GetAllDeviceTokensByDomainAsync(domain));
        }

        [Fact]
        public async Task GetAllDeviceTokensByDomainAsync_should_throw_exception_and_log_error_when_push_contacts_cannot_be_getter_from_storage()
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

            var loggerMock = new Mock<ILogger<PushContactService>>();

            var sut = CreateSut(
                mongoClientMock.Object,
                Options.Create(pushMongoContextSettings),
                logger: loggerMock.Object);

            // Act
            // Assert
            await Assert.ThrowsAsync<Exception>(() => sut.GetAllDeviceTokensByDomainAsync(domain));

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
        public async Task GetAllDeviceTokensByDomainAsync_should_return_device_tokens_filtered_by_domain()
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
            var result = await sut.GetAllDeviceTokensByDomainAsync(domainFilter);

            // Assert
            Assert.All(result, x => allPushContactDocuments
                                    .Single(y => y[PushContactDocumentProps.DomainPropName].AsString == domainFilter && y[PushContactDocumentProps.DeviceTokenPropName].AsString == x));
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

            var loggerMock = new Mock<ILogger<PushContactService>>();

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

        [Fact]
        public async Task GetEnabledByVisitorGuid_should_throw_exception_and_log_error_when_push_contacts_cannot_be_getter_from_storage()
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

            var loggerMock = new Mock<ILogger<PushContactService>>();

            var sut = CreateSut(
                mongoClientMock.Object,
                Options.Create(pushMongoContextSettings),
                logger: loggerMock.Object);

            // Act
            // Assert
            await Assert.ThrowsAsync<Exception>(() => sut.GetEnabledByVisitorGuid(domain, visitorGuid));

            loggerMock.Verify(
                x => x.Log(
                    It.Is<LogLevel>(l => l == LogLevel.Error),
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString() == $"Error getting active {nameof(PushContactModel)}s by {nameof(visitorGuid)} {visitorGuid}"),
                    It.IsAny<Exception>(),
                    It.Is<Func<It.IsAnyType, Exception, string>>((v, t) => true)),
                Times.Once);
        }

        [Fact]
        public async Task GetEnabledByVisitorGuid_should_return_a_boolean()
        {
            // Arrange
            List<BsonDocument> allPushContactDocuments = FakePushContactDocuments(10);

            var random = new Random();
            int randomPushContactIndex = random.Next(allPushContactDocuments.Count);
            var visitorGuidFilter = allPushContactDocuments[randomPushContactIndex][PushContactDocumentProps.VisitorGuidPropName].AsString;
            var domainFilter = allPushContactDocuments[randomPushContactIndex][PushContactDocumentProps.DomainPropName].AsString;

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
            var result = await sut.GetEnabledByVisitorGuid(domainFilter, visitorGuidFilter);

            // Assert
            Assert.IsType<bool>(result);
        }

        [Fact]
        public async Task UpdateSubscriptionAsync_should_throw_ArgumentException_when_device_token_is_null()
        {
            // Arrange
            var sut = CreateSut();

            // Act
            // Assert
            var result = await Assert.ThrowsAsync<ArgumentException>(() => sut.UpdateSubscriptionAsync(null, new SubscriptionDTO()));
        }

        [Fact]
        public async Task UpdateSubscriptionAsync_should_throw_ArgumentException_when_subscription_is_null()
        {
            // Arrange
            var fixture = new Fixture();
            var deviceToken = fixture.Create<string>();

            var sut = CreateSut();

            // Act
            // Assert
            var result = await Assert.ThrowsAsync<ArgumentException>(() => sut.UpdateSubscriptionAsync(deviceToken, null));
        }

        [Theory]
        [InlineData(null, null, null)]
        [InlineData("aEndpoint", null, null)]
        [InlineData("aEndpoint", "aP256DH", null)]
        [InlineData("aEndpoint", null, "aAuth")]
        [InlineData(null, null, "aAuth")]
        public async Task UpdateSubscriptionAsync_should_throw_ArgumentException_when_subscription_fields_are_null_or_empty(
            string endpoint,
            string p256dh,
            string auth
        )
        {
            // Arrange
            var fixture = new Fixture();
            var deviceToken = fixture.Create<string>();

            var sut = CreateSut();

            var subscription = new SubscriptionDTO()
            {
                EndPoint = endpoint,
                Keys = new SubscriptionKeys()
                {
                    P256DH = p256dh,
                    Auth = auth,
                },
            };

            // Act
            // Assert
            var result = await Assert.ThrowsAsync<ArgumentException>(() => sut.UpdateSubscriptionAsync(deviceToken, subscription));
        }

        [Theory]
        [InlineData("www.missing-protocol.com")]
        [InlineData("http:/www.missing-slash-in-protocol.com")]
        [InlineData("http//www.missing-colon-in-protocol.com")]
        [InlineData("https://www .white-space-in-domain.com")]
        [InlineData("https://www.whit-spaces-in-path.com/path with spaces")]
        public async Task UpdateSubscriptionAsync_should_throw_ArgumentException_when_subscription_endpoint_is_bad_formed(string endpoint)
        {
            // Arrange
            var fixture = new Fixture();
            var deviceToken = fixture.Create<string>();

            var sut = CreateSut();

            var subscription = new SubscriptionDTO()
            {
                EndPoint = endpoint,
                Keys = new SubscriptionKeys()
                {
                    P256DH = fixture.Create<string>(),
                    Auth = fixture.Create<string>(),
                },
            };

            // Act
            // Assert
            var result = await Assert.ThrowsAsync<ArgumentException>(() => sut.UpdateSubscriptionAsync(deviceToken, subscription));
        }

        [Fact]
        public async Task UpdateSubscriptionAsync_should_throw_exception_and_log_error_when_push_contact_model_cannot_be_updated()
        {
            // Arrange
            var fixture = new Fixture();

            var deviceToken = fixture.Create<string>();
            var subscription = new SubscriptionDTO()
            {
                EndPoint = "https://www.test-endpoint.com",
                Keys = new SubscriptionKeys()
                {
                    P256DH = fixture.Create<string>(),
                    Auth = fixture.Create<string>(),
                },
            };

            var pushMongoContextSettings = fixture.Create<PushMongoContextSettings>();

            var pushContactsCollectionMock = new Mock<IMongoCollection<BsonDocument>>();
            pushContactsCollectionMock
                .Setup(x => x.UpdateOneAsync(
                        It.IsAny<FilterDefinition<BsonDocument>>(),
                        It.IsAny<UpdateDefinition<BsonDocument>>(),
                        default,
                        default
                    )
                )
                .ThrowsAsync(new Exception());

            var mongoDatabaseMock = new Mock<IMongoDatabase>();
            mongoDatabaseMock
                .Setup(x => x.GetCollection<BsonDocument>(pushMongoContextSettings.PushContactsCollectionName, null))
                .Returns(pushContactsCollectionMock.Object);

            var mongoClientMock = new Mock<IMongoClient>();
            mongoClientMock
                .Setup(x => x.GetDatabase(pushMongoContextSettings.DatabaseName, null))
                .Returns(mongoDatabaseMock.Object);

            var loggerMock = new Mock<ILogger<PushContactService>>();

            var sut = CreateSut(
                mongoClientMock.Object,
                Options.Create(pushMongoContextSettings),
                logger: loggerMock.Object);

            var errorMsgExpected = $@"Error updating {nameof(PushContactModel)} with {nameof(deviceToken)} {deviceToken}." +
                    $" The {PushContactDocumentProps.Subscription_PropName} can not be updated with following value:";

            // Act
            // Assert
            await Assert.ThrowsAsync<Exception>(() => sut.UpdateSubscriptionAsync(deviceToken, subscription));
            loggerMock.Verify(
                x => x.Log(
                    It.Is<LogLevel>(l => l == LogLevel.Error),
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains(errorMsgExpected)),
                    It.IsAny<Exception>(),
                    It.Is<Func<It.IsAnyType, Exception, string>>((v, t) => true)),
                Times.Once);
        }

        [Fact]
        public async Task UpdateSubscriptionAsync_should_return_ok_when_push_contact_model_can_be_updated()
        {
            // Arrange
            var fixture = new Fixture();

            var deviceToken = fixture.Create<string>();
            var subscription = new SubscriptionDTO()
            {
                EndPoint = "https://www.test-endpoint.com",
                Keys = new SubscriptionKeys()
                {
                    P256DH = fixture.Create<string>(),
                    Auth = fixture.Create<string>(),
                },
            };

            var updateResultMock = new Mock<UpdateResult>();
            updateResultMock.SetupGet(r => r.ModifiedCount).Returns(1);

            var pushMongoContextSettings = fixture.Create<PushMongoContextSettings>();

            var pushContactsCollection = new Mock<IMongoCollection<BsonDocument>>();
            pushContactsCollection
                .Setup(x => x.UpdateOneAsync(
                        It.IsAny<FilterDefinition<BsonDocument>>(),
                        It.IsAny<UpdateDefinition<BsonDocument>>(),
                        default,
                        default
                    )
                )
                .ReturnsAsync(updateResultMock.Object);

            var mongoDatabase = new Mock<IMongoDatabase>();
            mongoDatabase
                .Setup(x => x.GetCollection<BsonDocument>(pushMongoContextSettings.PushContactsCollectionName, null))
                .Returns(pushContactsCollection.Object);

            var mongoClient = new Mock<IMongoClient>();
            mongoClient
                .Setup(x => x.GetDatabase(pushMongoContextSettings.DatabaseName, null))
                .Returns(mongoDatabase.Object);

            var sut = CreateSut(
                mongoClient.Object,
                Options.Create(pushMongoContextSettings));

            // Act
            var result = await sut.UpdateSubscriptionAsync(deviceToken, subscription);

            // Assert
            Assert.True(result);
        }

        [Fact]
        public async Task GetPushContactDomainAsync_should_return_null_when_push_contact_does_not_exist()
        {
            // Arrange
            var fixture = new Fixture();
            var pushContactId = fixture.Create<string>();

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
            var result = await sut.GetPushContactDomainAsync(pushContactId);

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public async Task GetPushContactDomainAsync_should_return_domain_when_push_contact_exists()
        {
            // Arrange
            var fixture = new Fixture();
            var pushContactId = fixture.Create<string>();

            var expectedDomain = "example.com";
            var document = new BsonDocument
            {
                { PushContactDocumentProps.DomainPropName, expectedDomain }
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
            var result = await sut.GetPushContactDomainAsync(pushContactId);

            // Assert
            Assert.Equal(expectedDomain, result);
        }

        [Fact]
        public async Task GetPushContactDomainAsync_should_log_error_when_mongo_exception_is_thrown()
        {
            // Arrange
            var fixture = new Fixture();
            var pushContactId = fixture.Create<string>();

            var mongoClientMock = new Mock<IMongoClient>();
            var databaseMock = new Mock<IMongoDatabase>();
            var collectionMock = new Mock<IMongoCollection<BsonDocument>>();
            var loggerMock = new Mock<ILogger<PushContactService>>();

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

            // Act & Assert
            await Assert.ThrowsAsync<MongoException>(() => sut.GetPushContactDomainAsync(pushContactId));

            loggerMock.Verify(
                x => x.Log(
                    It.Is<LogLevel>(l => l == LogLevel.Error),
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString() == $"MongoException getting Push-Contact by pushContactId {pushContactId}"),
                    It.IsAny<Exception>(),
                    It.Is<Func<It.IsAnyType, Exception, string>>((v, t) => true)),
                Times.Once);
        }

        [Fact]
        public async Task GetPushContactDomainAsync_should_log_error_when_general_exception_is_thrown()
        {
            // Arrange
            var fixture = new Fixture();
            var pushContactId = fixture.Create<string>();

            var mongoClientMock = new Mock<IMongoClient>();
            var databaseMock = new Mock<IMongoDatabase>();
            var collectionMock = new Mock<IMongoCollection<BsonDocument>>();
            var loggerMock = new Mock<ILogger<PushContactService>>();

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

            // Act & Assert
            await Assert.ThrowsAsync<Exception>(() => sut.GetPushContactDomainAsync(pushContactId));

            loggerMock.Verify(
                x => x.Log(
                    It.Is<LogLevel>(l => l == LogLevel.Error),
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString() == $"Unexpected error getting Push-Contact by {nameof(pushContactId)} {pushContactId}"),
                    It.IsAny<Exception>(),
                    It.Is<Func<It.IsAnyType, Exception, string>>((v, t) => true)),
                Times.Once);
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

        private MongoWriteException CreateMongoWriteException()
        {
            var writeError = typeof(WriteError).GetConstructors(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .FirstOrDefault(c => c.GetParameters().Length == 4)?
                .Invoke(new object[]
                {
                    ServerErrorCategory.DuplicateKey,
                    11000,
                    "E11000 duplicate key error collection: push-local.push-contacts index: device_token_1 dup key",
                    new BsonDocument()
                });

            var endPoint = new DnsEndPoint("localhost", 27017);
            var serverId = new ServerId(new ClusterId(1), endPoint);
            var connectionId = new ConnectionId(serverId, 0);

            return new MongoWriteException(connectionId, (WriteError)writeError, null, null);
        }
    }
}
