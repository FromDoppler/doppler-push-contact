using Doppler.PushContact.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using Doppler.PushContact.ApiModels;
using System.Linq.Expressions;

namespace Doppler.PushContact.Services
{
    public class PushContactService : IPushContactService
    {
        private readonly IMongoClient _mongoClient;
        private readonly IOptions<PushMongoContextSettings> _pushMongoContextSettings;
        private readonly IDeviceTokenValidator _deviceTokenValidator;
        private readonly ILogger<PushContactService> _logger;

        public PushContactService(
            IMongoClient mongoClient,
            IOptions<PushMongoContextSettings> pushMongoContextSettings,
            IDeviceTokenValidator deviceTokenValidator,
            ILogger<PushContactService> logger)
        {
            _mongoClient = mongoClient;
            _pushMongoContextSettings = pushMongoContextSettings;
            _deviceTokenValidator = deviceTokenValidator;
            _logger = logger;
        }

        public async Task AddAsync(PushContactModel pushContactModel)
        {
            if (pushContactModel == null)
            {
                throw new ArgumentNullException(nameof(pushContactModel));
            }

            if (!await _deviceTokenValidator.IsValidAsync(pushContactModel.DeviceToken))
            {
                throw new ArgumentException($"{nameof(pushContactModel.DeviceToken)} is not valid");
            }

            var now = DateTime.UtcNow;
            var key = ObjectId.GenerateNewId(now).ToString();

            var pushContactDocument = new BsonDocument {
                { PushContactDocumentProps.IdPropName, key },
                { PushContactDocumentProps.DomainPropName, pushContactModel.Domain },
                { PushContactDocumentProps.DeviceTokenPropName, pushContactModel.DeviceToken },
                { PushContactDocumentProps.EmailPropName, string.IsNullOrEmpty(pushContactModel.Email) ? BsonNull.Value : pushContactModel.Email },
                { PushContactDocumentProps.DeletedPropName, false },
                { PushContactDocumentProps.ModifiedPropName, now }
            };

            try
            {
                await PushContacts.InsertOneAsync(pushContactDocument);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, @$"Error inserting {nameof(pushContactModel)}
with following {nameof(pushContactModel.DeviceToken)}: {pushContactModel.DeviceToken}");

                throw;
            }
        }

        public async Task UpdateEmailAsync(string deviceToken, string email)
        {
            if (deviceToken == null)
            {
                throw new ArgumentNullException(nameof(deviceToken));
            }

            if (email == null)
            {
                throw new ArgumentNullException(nameof(email));
            }

            var filter = Builders<BsonDocument>.Filter.Eq(PushContactDocumentProps.DeviceTokenPropName, deviceToken)
                & Builders<BsonDocument>.Filter.Eq(PushContactDocumentProps.DeletedPropName, false);

            var updateDefinition = Builders<BsonDocument>.Update
                .Set(PushContactDocumentProps.EmailPropName, email)
                .Set(PushContactDocumentProps.ModifiedPropName, DateTime.UtcNow);

            try
            {
                await PushContacts.UpdateOneAsync(filter, updateDefinition);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, @$"Error updating {nameof(PushContactModel)}
with {nameof(deviceToken)} {deviceToken}. {PushContactDocumentProps.EmailPropName} can not be updated with following value: {email}");

                throw;
            }
        }

        public async Task<IEnumerable<PushContactModel>> GetAsync(PushContactFilter pushContactFilter)
        {
            if (pushContactFilter == null)
            {
                throw new ArgumentNullException(nameof(pushContactFilter));
            }

            if (string.IsNullOrEmpty(pushContactFilter.Domain))
            {
                throw new ArgumentException(
                    $"'{nameof(pushContactFilter.Domain)}' cannot be null or empty", nameof(pushContactFilter.Domain));
            }

            if (pushContactFilter.ModifiedFrom > pushContactFilter.ModifiedTo)
            {
                throw new ArgumentException(
                    $"'{nameof(pushContactFilter.ModifiedFrom)}' cannot be greater than '{nameof(pushContactFilter.ModifiedTo)}'");
            }

            var filterBuilder = Builders<BsonDocument>.Filter;

            var filter = filterBuilder.Eq(PushContactDocumentProps.DomainPropName, pushContactFilter.Domain);

            if (pushContactFilter.Email != null)
            {
                filter &= filterBuilder.Eq(PushContactDocumentProps.EmailPropName, pushContactFilter.Email);
            }

            if (pushContactFilter.ModifiedFrom != null)
            {
                filter &= filterBuilder.Gte(PushContactDocumentProps.ModifiedPropName, pushContactFilter.ModifiedFrom);
            }

            if (pushContactFilter.ModifiedTo != null)
            {
                filter &= filterBuilder.Lte(PushContactDocumentProps.ModifiedPropName, pushContactFilter.ModifiedTo);
            }

            filter &= !filterBuilder.Eq(PushContactDocumentProps.DeletedPropName, true);

            try
            {
                var pushContactsFiltered = await (await PushContacts.FindAsync<BsonDocument>(filter)).ToListAsync();

                return pushContactsFiltered
                    .Select(x =>
                    {
                        return new PushContactModel
                        {
                            Domain = x.GetValue(PushContactDocumentProps.DomainPropName, null)?.AsString,
                            DeviceToken = x.GetValue(PushContactDocumentProps.DeviceTokenPropName, null)?.AsString,
                            Email = x.GetValue(PushContactDocumentProps.EmailPropName, null)?.AsString
                        };
                    });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting {nameof(PushContactModel)}s");

                throw;
            }
        }

        public async Task<long> DeleteByDeviceTokenAsync(IEnumerable<string> deviceTokens)
        {
            if (deviceTokens == null || !deviceTokens.Any())
            {
                throw new ArgumentException(
                    $"'{nameof(deviceTokens)}' cannot be null or empty", nameof(deviceTokens));
            }

            var filter = Builders<BsonDocument>.Filter.AnyIn(PushContactDocumentProps.DeviceTokenPropName, deviceTokens);

            var update = new BsonDocument("$set", new BsonDocument
                {
                    { PushContactDocumentProps.DeletedPropName, true },
                    { PushContactDocumentProps.ModifiedPropName, DateTime.UtcNow }
                });

            try
            {
                var result = await PushContacts.UpdateManyAsync(filter, update);

                return result.IsModifiedCountAvailable ? result.ModifiedCount : default;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error deleting {nameof(PushContactModel)}s");

                throw;
            }
        }

        public async Task AddHistoryEventsAsync(IEnumerable<PushContactHistoryEvent> pushContactHistoryEvents)
        {
            if (pushContactHistoryEvents == null || !pushContactHistoryEvents.Any())
            {
                throw new ArgumentException(
                    $"'{nameof(pushContactHistoryEvents)}' cannot be null or empty", nameof(pushContactHistoryEvents));
            }

            var now = DateTime.UtcNow;

            var updateRequest = pushContactHistoryEvents
                .Select(x =>
                {
                    var historyEvent = new BsonDocument {
                        { PushContactDocumentProps.HistoryEvents_SentSuccessPropName, x.SentSuccess },
                        { PushContactDocumentProps.HistoryEvents_EventDatePropName, x.EventDate },
                        { PushContactDocumentProps.HistoryEvents_InsertedDatePropName, now },
                        { PushContactDocumentProps.HistoryEvents_DetailsPropName, string.IsNullOrEmpty(x.Details) ? BsonNull.Value : x.Details },
                        { PushContactDocumentProps.HistoryEvents_MessageIdPropName, new BsonBinaryData(x.MessageId, GuidRepresentation.Standard) }
                    };

                    var filter = Builders<BsonDocument>.Filter.Eq(PushContactDocumentProps.DeviceTokenPropName, x.DeviceToken);

                    var update = Builders<BsonDocument>.Update.Push(PushContactDocumentProps.HistoryEventsPropName, historyEvent);

                    return new UpdateOneModel<BsonDocument>(filter, update);
                });

            try
            {
                await PushContacts.BulkWriteAsync(updateRequest);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error adding {nameof(PushContactHistoryEvent)}s");

                throw;
            }
        }

        public async Task<IEnumerable<string>> GetAllDeviceTokensByDomainAsync(string domain)
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
                .Exclude(PushContactDocumentProps.IdPropName)
            };

            try
            {
                var pushContactsFiltered = await (await PushContacts.FindAsync(filter, options)).ToListAsync();

                return pushContactsFiltered
                    .Select(x => x.GetValue(PushContactDocumentProps.DeviceTokenPropName).AsString);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting {nameof(PushContactModel)}s by {nameof(domain)} {domain}");

                throw;
            }
        }

        public async Task<ApiPage<DomainInfo>> GetDomains(int page, int per_page)
        {
            try
            {
                var domainsFiltered = await PushContacts.Aggregate()
                    .Group(new BsonDocument
                    {
                        { "_id", $"${PushContactDocumentProps.DomainPropName}" },
                        { "PushContactActiveQuantity",
                            new BsonDocument("$sum",
                                new BsonDocument("$cond",
                                    new BsonArray
                                    {
                                        new BsonDocument("$eq",
                                            new BsonArray
                                            {
                                                "$deleted",
                                                false
                                            }),
                                            1,
                                            0
                        }))},
                        { "PushContactInactiveQuantity",
                            new BsonDocument("$sum",
                                new BsonDocument("$cond",
                                    new BsonArray
                                    {
                                        new BsonDocument("$eq",
                                            new BsonArray
                                            {
                                                "$deleted",
                                                true
                                            }),
                                            1,
                                            0
                        }))}
                    })
                    .Sort(new BsonDocument("_id", 1))
                    .Project(new BsonDocument
                            {
                                    { "_id", 0 },
                                    {$"{PushContactDocumentProps.DomainPropName}", "$_id"},
                                    {"PushContactInactiveQuantity", 1},
                                    {"PushContactActiveQuantity", 1},
                            })
                    .Skip(page)
                    .Limit(per_page)
                    .ToListAsync();

                var newPage = page + domainsFiltered.Count;

                var domainList = domainsFiltered
                    .Select(x => new DomainInfo()
                    {
                        Name = x.GetValue(PushContactDocumentProps.DomainPropName, null)?.AsString,
                        PushContactActiveQuantity = x.GetValue("PushContactActiveQuantity", null).ToInt32(),
                        PushContactInactiveQuantity = x.GetValue("PushContactInactiveQuantity", null).ToInt32()
                    }
                    )
                    .ToList();

                return new ApiPage<DomainInfo>(domainList, newPage, per_page);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting the quantity of queries by domain");

                throw;
            }
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
