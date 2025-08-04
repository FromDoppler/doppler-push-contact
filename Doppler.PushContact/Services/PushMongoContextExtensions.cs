using Doppler.PushContact.Models.Entities;
using Doppler.PushContact.Services.Messages;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;

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
                    Builders<BsonDocument>.IndexKeys.Ascending(PushContactDocumentProps.DeviceTokenPropName),
                    new CreateIndexOptions { Unique = true });
                    pushContacts.Indexes.CreateOne(deviceTokenAsUniqueIndex);

                    var domainAsSingleFieldIndex = new CreateIndexModel<BsonDocument>(
                    Builders<BsonDocument>.IndexKeys.Ascending(PushContactDocumentProps.DomainPropName),
                    new CreateIndexOptions { Unique = false });
                    pushContacts.Indexes.CreateOne(domainAsSingleFieldIndex);

                    var domainAndDeletedIndex = new CreateIndexModel<BsonDocument>(
                    Builders<BsonDocument>.IndexKeys
                        .Ascending(PushContactDocumentProps.DomainPropName)
                        .Ascending(PushContactDocumentProps.DeletedPropName),
                    new CreateIndexOptions { Unique = false });
                    pushContacts.Indexes.CreateOne(domainAndDeletedIndex);

                    var deletedAndDomainAndVisitorGuidIndex = new CreateIndexModel<BsonDocument>(
                    Builders<BsonDocument>.IndexKeys
                        .Ascending(PushContactDocumentProps.DeletedPropName)
                        .Ascending(PushContactDocumentProps.DomainPropName)
                        .Ascending(PushContactDocumentProps.VisitorGuidPropName),
                    new CreateIndexOptions { Unique = false });
                    pushContacts.Indexes.CreateOne(deletedAndDomainAndVisitorGuidIndex);

                    // domains indexes
                    var domains = database.GetCollection<BsonDocument>(pushMongoContextSettings.DomainsCollectionName);

                    var domainNameAsUniqueIndex = new CreateIndexModel<BsonDocument>(
                    Builders<BsonDocument>.IndexKeys.Ascending(DomainDocumentProps.DomainNamePropName),
                    new CreateIndexOptions { Unique = true });
                    domains.Indexes.CreateOne(domainNameAsUniqueIndex);

                    // messages indexes
                    var messages = database.GetCollection<BsonDocument>(pushMongoContextSettings.MessagesCollectionName);

                    var messageIdAsUniqueIndex = new CreateIndexModel<BsonDocument>(
                    Builders<BsonDocument>.IndexKeys
                    .Ascending(MessageDocumentProps.DomainPropName)
                    .Ascending(MessageDocumentProps.MessageIdPropName),
                    new CreateIndexOptions { Unique = true });
                    messages.Indexes.CreateOne(messageIdAsUniqueIndex);

                    return mongoClient;
                });

            return services;
        }
    }
}
