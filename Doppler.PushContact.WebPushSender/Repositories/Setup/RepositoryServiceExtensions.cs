using Doppler.PushContact.Models.Entities;
using Doppler.PushContact.WebPushSender.Repositories.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Doppler.PushContact.WebPushSender.Repositories.Setup
{
    public static class RepositoryServiceExtensions
    {
        public static IServiceCollection AddMongoDBRepositoryService(this IServiceCollection services, IConfiguration configuration)
        {
            var repositorySettingsSection = configuration.GetSection(nameof(RepositorySettings));
            services.Configure<RepositorySettings>(repositorySettingsSection);

            var repositorySettings = new RepositorySettings();
            repositorySettingsSection.Bind(repositorySettings);

            var mongoUrlBuilder = new MongoUrlBuilder(repositorySettings.ConnectionUrl);
            mongoUrlBuilder.DatabaseName ??= repositorySettings.DatabaseName;
            mongoUrlBuilder.Password ??= repositorySettings.SecretPassword;

            var mongoUrl = mongoUrlBuilder.ToMongoUrl();
            var mongoClient = new MongoClient(mongoUrl);
            var mongoDatabase = mongoClient.GetDatabase(mongoUrl.DatabaseName);

            ConfigureIndexes(mongoDatabase, repositorySettings);

            services.AddSingleton<IMongoClient>(mongoClient);
            services.AddScoped(x => mongoClient.GetDatabase(mongoUrl.DatabaseName));

            services.AddScoped<IWebPushEventRepository, WebPushEventRepository>();
            services.AddScoped<IPushContactRepository, PushContactRepository>();

            return services;
        }

        private static void ConfigureIndexes(IMongoDatabase database, RepositorySettings repositorySettings)
        {
            var webPushEventCollection = database.GetCollection<BsonDocument>(repositorySettings.WebPushEventCollectionName);

            var indexKeysDefinitionBuilder = Builders<BsonDocument>.IndexKeys;

            var indexModelPushContactId = new CreateIndexModel<BsonDocument>(
                indexKeysDefinitionBuilder.Ascending(WebPushEventDocumentProps.PushContactId_PropName)
            );

            var indexModelMessageIdAndType = new CreateIndexModel<BsonDocument>(
                indexKeysDefinitionBuilder
                    .Ascending(WebPushEventDocumentProps.MessageId_PropName)
                    .Ascending(WebPushEventDocumentProps.Type_PropName)
            );

            var indexModel_Domain_Date_Type_SubType = new CreateIndexModel<BsonDocument>(
                indexKeysDefinitionBuilder
                    .Ascending(WebPushEventDocumentProps.Domain_PropName)
                    .Ascending(WebPushEventDocumentProps.Date_PropName)
                    .Ascending(WebPushEventDocumentProps.Type_PropName)
                    .Ascending(WebPushEventDocumentProps.SubType_PropName)
            );

            webPushEventCollection.Indexes.CreateMany([
                indexModelPushContactId,
                indexModelMessageIdAndType,
                indexModel_Domain_Date_Type_SubType,
            ]);
        }
    }
}
