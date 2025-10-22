using Doppler.PushContact.Models.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;
using System;

namespace Doppler.PushContact.Services
{
    public static class PushMongoContextExtensions
    {
        public static IServiceCollection AddPushMongoContext(this IServiceCollection services, IConfiguration configuration)
        {
            var pushMongoContextSettingsSection = configuration.GetSection(nameof(PushMongoContextSettings));

            services.Configure<PushMongoContextSettings>(pushMongoContextSettingsSection);

            var pushMongoContextSettings = new PushMongoContextSettings();
            pushMongoContextSettingsSection.Bind(pushMongoContextSettings);

            var mongoUrlBuilder = new MongoUrlBuilder(pushMongoContextSettings.ConnectionString)
            {
                Password = pushMongoContextSettings.Password,
                DatabaseName = pushMongoContextSettings.DatabaseName
            };

            var mongoUrl = mongoUrlBuilder.ToMongoUrl();

            services.AddSingleton<IMongoClient>(x =>
                {
                    var mongoClient = new MongoClient(mongoUrl);
                    var database = mongoClient.GetDatabase(pushMongoContextSettings.DatabaseName);

                    // push-contacts indexes
                    var pushContacts = database.GetCollection<BsonDocument>(pushMongoContextSettings.PushContactsCollectionName);

                    var deviceTokenAsUniqueIndex = new CreateIndexModel<BsonDocument>(
                        Builders<BsonDocument>.IndexKeys
                            .Ascending(PushContactDocumentProps.DeviceTokenPropName),
                        new CreateIndexOptions { Unique = true }
                    );
                    pushContacts.Indexes.CreateOne(deviceTokenAsUniqueIndex);

                    var domainAndDeletedIndex = new CreateIndexModel<BsonDocument>(
                        Builders<BsonDocument>.IndexKeys
                            .Ascending(PushContactDocumentProps.DomainPropName)
                            .Ascending(PushContactDocumentProps.DeletedPropName),
                        new CreateIndexOptions { Unique = false }
                    );
                    pushContacts.Indexes.CreateOne(domainAndDeletedIndex);

                    var deletedAndDomainAndVisitorGuidIndex = new CreateIndexModel<BsonDocument>(
                        Builders<BsonDocument>.IndexKeys
                            .Ascending(PushContactDocumentProps.DeletedPropName)
                            .Ascending(PushContactDocumentProps.DomainPropName)
                            .Ascending(PushContactDocumentProps.VisitorGuidPropName),
                        new CreateIndexOptions { Unique = false }
                    );
                    pushContacts.Indexes.CreateOne(deletedAndDomainAndVisitorGuidIndex);

                    var VisitorGuidAndDomainAndDeletedIndex = new CreateIndexModel<BsonDocument>(
                        Builders<BsonDocument>.IndexKeys
                            .Ascending(PushContactDocumentProps.VisitorGuidPropName)
                            .Ascending(PushContactDocumentProps.DomainPropName)
                            .Ascending(PushContactDocumentProps.DeletedPropName),
                        new CreateIndexOptions { Unique = false }
                    );
                    pushContacts.Indexes.CreateOne(VisitorGuidAndDomainAndDeletedIndex);

                    // domains indexes
                    var domains = database.GetCollection<BsonDocument>(pushMongoContextSettings.DomainsCollectionName);

                    var domainNameAsUniqueIndex = new CreateIndexModel<BsonDocument>(
                        Builders<BsonDocument>.IndexKeys
                            .Ascending(DomainDocumentProps.DomainNamePropName),
                        new CreateIndexOptions { Unique = true }
                    );
                    domains.Indexes.CreateOne(domainNameAsUniqueIndex);

                    // messages indexes
                    var messages = database.GetCollection<BsonDocument>(pushMongoContextSettings.MessagesCollectionName);

                    var messageIdAsUniqueIndex = new CreateIndexModel<BsonDocument>(
                        Builders<BsonDocument>.IndexKeys
                            .Ascending(MessageDocumentProps.MessageIdPropName),
                        new CreateIndexOptions { Unique = true }
                    );
                    messages.Indexes.CreateOne(messageIdAsUniqueIndex);

                    var messageIdAndDomainAsUniqueIndex = new CreateIndexModel<BsonDocument>(
                        Builders<BsonDocument>.IndexKeys
                            .Ascending(MessageDocumentProps.DomainPropName)
                            .Ascending(MessageDocumentProps.MessageIdPropName),
                        new CreateIndexOptions { Unique = true }
                    );
                    messages.Indexes.CreateOne(messageIdAndDomainAsUniqueIndex);

                    var domainAndInsertedDateIndex = new CreateIndexModel<BsonDocument>(
                        Builders<BsonDocument>.IndexKeys
                            .Ascending(MessageDocumentProps.DomainPropName)
                            .Ascending(MessageDocumentProps.InsertedDatePropName),
                        new CreateIndexOptions { Unique = false }
                    );
                    messages.Indexes.CreateOne(domainAndInsertedDateIndex);

                    // messageStats indexes
                    var messageStatsCollection = database.GetCollection<BsonDocument>(pushMongoContextSettings.MessageStatsCollectionName);

                    var indexModel_Domain_MessageId_Date_forMessageStats = new CreateIndexModel<BsonDocument>(
                        Builders<BsonDocument>.IndexKeys
                            .Ascending(MessageStatsDocumentProps.Domain_PropName)
                            .Ascending(MessageStatsDocumentProps.MessageId_PropName)
                            .Ascending(MessageStatsDocumentProps.Date_PropName),
                        new CreateIndexOptions { Unique = true }
                    );

                    var indexModel_Domain_Date_forMessageStats = new CreateIndexModel<BsonDocument>(
                        Builders<BsonDocument>.IndexKeys
                            .Ascending(MessageStatsDocumentProps.Domain_PropName)
                            .Ascending(MessageStatsDocumentProps.Date_PropName)
                    );

                    var indexModel_MessageId_Date_forMessageStats = new CreateIndexModel<BsonDocument>(
                        Builders<BsonDocument>.IndexKeys
                            .Ascending(MessageStatsDocumentProps.MessageId_PropName)
                            .Ascending(MessageStatsDocumentProps.Date_PropName)
                    );

                    var index_Date_TTL_forMessageStats = new CreateIndexModel<BsonDocument>(
                        Builders<BsonDocument>.IndexKeys.Ascending(MessageStatsDocumentProps.Date_PropName),
                        new CreateIndexOptions
                        {
                            ExpireAfter = TimeSpan.FromDays(360) // 1 year
                        }
                    );

                    messageStatsCollection.Indexes.CreateMany([
                        indexModel_Domain_MessageId_Date_forMessageStats,
                        indexModel_Domain_Date_forMessageStats,
                        indexModel_MessageId_Date_forMessageStats,
                        index_Date_TTL_forMessageStats,
                    ]);

                    return mongoClient;
                });

            return services;
        }
    }
}
