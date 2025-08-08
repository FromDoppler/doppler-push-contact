using Doppler.PushContact.DTOs;
using Doppler.PushContact.Models.DTOs;
using Doppler.PushContact.Models.Entities;
using Doppler.PushContact.Repositories.Interfaces;
using Doppler.PushContact.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Doppler.PushContact.Repositories
{
    public class PushContactRepository : IPushContactRepository
    {
        private readonly IMongoClient _mongoClient;
        private readonly IOptions<PushMongoContextSettings> _pushMongoContextSettings;
        private readonly ILogger<PushContactRepository> _logger;

        public PushContactRepository(
            IMongoClient mongoClient,
            IOptions<PushMongoContextSettings> pushMongoContextSettings,
            ILogger<PushContactRepository> logger)
        {
            _mongoClient = mongoClient;
            _pushMongoContextSettings = pushMongoContextSettings;
            _logger = logger;
        }

        public async Task<IEnumerable<SubscriptionInfoDTO>> GetAllSubscriptionInfoByDomainAsync(string domain)
        {
            if (string.IsNullOrEmpty(domain))
            {
                throw new ArgumentException($"'{nameof(domain)}' cannot be null or empty.", nameof(domain));
            }

            var filterBuilder = Builders<BsonDocument>.Filter;

            var filter = filterBuilder.Eq(PushContactDocumentProps.DomainPropName, domain)
                & filterBuilder.Eq(PushContactDocumentProps.DeletedPropName, false);

            var options = new FindOptions<BsonDocument>
            {
                Projection = Builders<BsonDocument>.Projection
                .Include(PushContactDocumentProps.DeviceTokenPropName)
                .Include(PushContactDocumentProps.Subscription_PropName)
                .Include(PushContactDocumentProps.IdPropName)
            };

            try
            {
                var pushContacts = await (await PushContacts.FindAsync(filter, options)).ToListAsync();
                return GetSubscriptionsInfoFromBsonDocuments(pushContacts);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting {nameof(SubscriptionInfoDTO)}s by {nameof(domain)} {domain}");

                throw;
            }
        }

        public async IAsyncEnumerable<SubscriptionInfoDTO> GetSubscriptionInfoByDomainAsStreamAsync(
        string domain,
            [EnumeratorCancellation] CancellationToken cancellationToken = default
        )
        {
            if (string.IsNullOrEmpty(domain))
            {
                throw new ArgumentException($"'{nameof(domain)}' cannot be null or empty.", nameof(domain));
            }

            var sizeFromConfig = _pushMongoContextSettings?.Value?.CursorBatchSize ?? 0;
            var batchSize = sizeFromConfig > 0 ? sizeFromConfig : 500;

            var filter = Builders<BsonDocument>.Filter.Eq(PushContactDocumentProps.DomainPropName, domain)
                & Builders<BsonDocument>.Filter.Eq(PushContactDocumentProps.DeletedPropName, false);

            var options = new FindOptions<BsonDocument>
            {
                Projection = Builders<BsonDocument>.Projection
                    .Include(PushContactDocumentProps.DeviceTokenPropName)
                    .Include(PushContactDocumentProps.Subscription_PropName)
                    .Include(PushContactDocumentProps.IdPropName),
                BatchSize = batchSize // numbers of documents readed in each cursor page
            };

            // define cursor
            using var cursor = await PushContacts.FindAsync(filter, options, cancellationToken);

            var index = 0;
            while (await cursor.MoveNextAsync(cancellationToken)) // read next page
            {
                index++;
                _logger.LogDebug("number of page read from DB: {PageIndex}", index);
                foreach (var doc in cursor.Current) // iterate docs of current page
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        _logger.LogWarning("Streaming cancelled while retrieving subscription info for domain: {Domain}", domain);
                        yield break;
                    }

                    // take advantage of IAsyncEnumerable<> to return one item at a time (streaming by one)
                    yield return GetSubscriptionInfoFromBson(doc);
                }
            }
        }

        public async Task<IEnumerable<SubscriptionInfoDTO>> GetAllSubscriptionInfoByVisitorGuidAsync(string visitorGuid)
        {
            if (string.IsNullOrEmpty(visitorGuid))
            {
                throw new ArgumentException($"'{nameof(visitorGuid)}' cannot be null or empty.", nameof(visitorGuid));
            }

            var filterBuilder = Builders<BsonDocument>.Filter;

            var filter = filterBuilder.Eq(PushContactDocumentProps.VisitorGuidPropName, visitorGuid)
                & filterBuilder.Eq(PushContactDocumentProps.DeletedPropName, false);

            var options = new FindOptions<BsonDocument>
            {
                Projection = Builders<BsonDocument>.Projection
                .Include(PushContactDocumentProps.DeviceTokenPropName)
                .Include(PushContactDocumentProps.Subscription_PropName)
                .Include(PushContactDocumentProps.IdPropName)
            };

            try
            {
                var pushContacts = await (await PushContacts.FindAsync(filter, options)).ToListAsync();
                return GetSubscriptionsInfoFromBsonDocuments(pushContacts);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting {nameof(SubscriptionInfoDTO)}s by {nameof(visitorGuid)} {visitorGuid}");

                throw;
            }
        }

        private SubscriptionInfoDTO GetSubscriptionInfoFromBson(BsonDocument doc)
        {
            var DEVTOKEN_PROP_NAME = PushContactDocumentProps.DeviceTokenPropName;
            var SUBSCRIPTION_PROP_NAME = PushContactDocumentProps.Subscription_PropName;
            var ENDPOINT_PROP_NAME = PushContactDocumentProps.Subscription_EndPoint_PropName;
            var AUTH_PROP_NAME = PushContactDocumentProps.Subscription_Auth_PropName;
            var P256DH_PROP_NAME = PushContactDocumentProps.Subscription_P256DH_PropName;
            var ID_PROP_NAME = PushContactDocumentProps.IdPropName;

            var deviceToken = doc.Contains(DEVTOKEN_PROP_NAME) && !doc[DEVTOKEN_PROP_NAME].IsBsonNull
                ? doc[DEVTOKEN_PROP_NAME].AsString
                : null;

            var pushContactId = doc.Contains(ID_PROP_NAME) && !doc[ID_PROP_NAME].IsBsonNull
                ? doc[ID_PROP_NAME].ToString()
                : null;

            SubscriptionDTO subscriptionModel = null;

            if (doc.Contains(SUBSCRIPTION_PROP_NAME) && !doc[SUBSCRIPTION_PROP_NAME].IsBsonNull)
            {
                var subscriptionDoc = doc[SUBSCRIPTION_PROP_NAME].AsBsonDocument;

                var endPoint = subscriptionDoc.Contains(ENDPOINT_PROP_NAME) && !subscriptionDoc[ENDPOINT_PROP_NAME].IsBsonNull
                    ? subscriptionDoc[ENDPOINT_PROP_NAME].AsString
                    : null;

                var auth = subscriptionDoc.Contains(AUTH_PROP_NAME) && !subscriptionDoc[AUTH_PROP_NAME].IsBsonNull
                    ? subscriptionDoc[AUTH_PROP_NAME].AsString
                    : null;

                var p256dh = subscriptionDoc.Contains(P256DH_PROP_NAME) && !subscriptionDoc[P256DH_PROP_NAME].IsBsonNull
                    ? subscriptionDoc[P256DH_PROP_NAME].AsString
                    : null;

                if (!string.IsNullOrEmpty(endPoint) && !string.IsNullOrEmpty(auth) && !string.IsNullOrEmpty(p256dh))
                {
                    subscriptionModel = new SubscriptionDTO
                    {
                        EndPoint = endPoint,
                        Keys = new SubscriptionKeys
                        {
                            Auth = auth,
                            P256DH = p256dh
                        }
                    };
                }
            }

            return new SubscriptionInfoDTO
            {
                DeviceToken = deviceToken,
                Subscription = subscriptionModel,
                PushContactId = pushContactId
            };
        }

        public async Task<ContactsStatsDTO> GetContactsStatsAsync(string domainName)
        {
            if (string.IsNullOrEmpty(domainName))
            {
                throw new ArgumentException($"'{nameof(domainName)}' cannot be null or empty.", nameof(domainName));
            }

            var filterBuilder = Builders<BsonDocument>.Filter;
            var filter = filterBuilder.Eq(PushContactDocumentProps.DomainPropName, domainName);

            var groupStage = new BsonDocument
            {
                { "$group", new BsonDocument
                    {
                        { "_id", "$" + PushContactDocumentProps.DeletedPropName },
                        { "count", new BsonDocument { { "$sum", 1 } } }
                    }
                }
            };

            var matchStage = new BsonDocument
            {
                { "$match", new BsonDocument
                    {
                        { PushContactDocumentProps.DomainPropName, domainName }
                    }
                }
            };

            var pipeline = new[] { matchStage, groupStage };

            try
            {
                var result = await PushContacts.AggregateAsync<BsonDocument>(pipeline);

                var stats = new ContactsStatsDTO();

                while (await result.MoveNextAsync())
                {
                    foreach (var doc in result.Current)
                    {
                        var deleted = doc["_id"].AsBoolean;
                        var count = doc["count"].AsInt32;

                        if (deleted)
                        {
                            stats.Deleted = count;
                        }
                        else
                        {
                            stats.Active = count;
                        }
                    }
                }

                stats.Total = stats.Deleted + stats.Active;
                stats.DomainName = domainName;

                return stats;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting contact stats for domain '{domainName}'");
                throw;
            }
        }

        private List<SubscriptionInfoDTO> GetSubscriptionsInfoFromBsonDocuments(List<BsonDocument> pushContacts)
        {
            return pushContacts.Select(GetSubscriptionInfoFromBson).ToList();
        }

        private IMongoCollection<BsonDocument> PushContacts
        {
            get
            {
                var database = _mongoClient.GetDatabase(_pushMongoContextSettings.Value.DatabaseName);
                return database.GetCollection<BsonDocument>(_pushMongoContextSettings.Value.PushContactsCollectionName);
            }
        }
    }
}
