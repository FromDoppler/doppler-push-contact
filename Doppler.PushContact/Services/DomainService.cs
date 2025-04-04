using Doppler.PushContact.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using System;
using System.Threading.Tasks;

namespace Doppler.PushContact.Services
{
    public class DomainService : IDomainService
    {
        private readonly IMongoClient _mongoClient;
        private readonly IOptions<PushMongoContextSettings> _pushMongoContextSettings;
        private readonly ILogger<DomainService> _logger;

        public DomainService(
            IMongoClient mongoClient,
            IOptions<PushMongoContextSettings> pushMongoContextSettings,
            ILogger<DomainService> logger)
        {

            _mongoClient = mongoClient;
            _pushMongoContextSettings = pushMongoContextSettings;
            _logger = logger;
        }

        public async Task UpsertAsync(Domain domain)
        {
            if (domain == null)
            {
                throw new ArgumentNullException(nameof(domain));
            }

            var now = DateTime.UtcNow;
            var key = ObjectId.GenerateNewId(now).ToString();

            var filter = Builders<BsonDocument>.Filter.Eq(DomainDocumentProps.DomainNamePropName, domain.Name);

            var upsertDefinition = Builders<BsonDocument>.Update
                .Set(DomainDocumentProps.IsPushFeatureEnabledPropName, domain.IsPushFeatureEnabled)
                .Set(DomainDocumentProps.ModifiedPropName, now)
                .Set(DomainDocumentProps.UsesExternalPushDomain, domain.UsesExternalPushDomain)
                .Set(DomainDocumentProps.ExternalPushDomain, domain.ExternalPushDomain)
                .SetOnInsert(DomainDocumentProps.IdPropName, key)
                .SetOnInsert(DomainDocumentProps.DomainNamePropName, domain.Name);

            try
            {
                await Domains.UpdateOneAsync(filter, upsertDefinition, new UpdateOptions { IsUpsert = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error upserting {nameof(Domain)} with {nameof(domain.Name)} {domain.Name}");

                throw;
            }
        }

        public async Task<Domain> GetByNameAsync(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException($"'{nameof(name)}' cannot be null or whitespace.", nameof(name));
            }

            var filter = Builders<BsonDocument>.Filter.Eq(DomainDocumentProps.DomainNamePropName, name);

            try
            {
                var domainDocument = await (await Domains.FindAsync<BsonDocument>(filter)).SingleOrDefaultAsync();

                if (domainDocument == null)
                {
                    return null;
                }

                return new Domain
                {
                    Name = domainDocument.GetValue(DomainDocumentProps.DomainNamePropName).AsString,
                    IsPushFeatureEnabled = domainDocument.GetValue(DomainDocumentProps.IsPushFeatureEnabledPropName).AsBoolean,
                    UsesExternalPushDomain = domainDocument.Contains(DomainDocumentProps.UsesExternalPushDomain)
                        ? domainDocument.GetValue(DomainDocumentProps.UsesExternalPushDomain).AsBoolean
                        : false,
                    ExternalPushDomain =
                        domainDocument.Contains(DomainDocumentProps.ExternalPushDomain) && domainDocument[DomainDocumentProps.ExternalPushDomain] != BsonNull.Value
                            ? domainDocument[DomainDocumentProps.ExternalPushDomain].AsString
                            : null,
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting {nameof(Domain)} with name equals to {name}");

                throw;
            }
        }

        private IMongoCollection<BsonDocument> Domains
        {
            get
            {
                var database = _mongoClient.GetDatabase(_pushMongoContextSettings.Value.DatabaseName);
                return database.GetCollection<BsonDocument>(_pushMongoContextSettings.Value.DomainsCollectionName);
            }
        }
    }
}
