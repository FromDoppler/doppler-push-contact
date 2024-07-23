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
using Doppler.PushContact.Services.Messages;
using Doppler.PushContact.DTOs;
using Doppler.PushContact.Models.Entities;

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

        private BsonDocument GetSubscriptionInfo(PushContactModel pushContactModel)
        {
            BsonDocument subscription = null;

            if (pushContactModel.Subscription != null
                && !string.IsNullOrEmpty(pushContactModel.Subscription.EndPoint)
                && pushContactModel.Subscription.Keys != null
                && !string.IsNullOrEmpty(pushContactModel.Subscription.Keys.P256DH)
                && !string.IsNullOrEmpty(pushContactModel.Subscription.Keys.Auth)
            )
            {
                subscription = new BsonDocument {
                    { PushContactDocumentProps.Subscription_EndPoint_PropName, pushContactModel.Subscription.EndPoint },
                    { PushContactDocumentProps.Subscription_P256DH_PropName, pushContactModel.Subscription.Keys.P256DH },
                    { PushContactDocumentProps.Subscription_Auth_PropName, pushContactModel.Subscription.Keys.Auth },
                };
            }

            return subscription;
        }

        public async Task AddAsync(PushContactModel pushContactModel)
        {
            if (pushContactModel == null)
            {
                throw new ArgumentNullException(nameof(pushContactModel));
            }

            // TODO: DeviceToken will be deprecated, remove this validation (should be added a similar validation to the subscription info?)
            if (!await _deviceTokenValidator.IsValidAsync(pushContactModel.DeviceToken))
            {
                throw new ArgumentException($"{nameof(pushContactModel.DeviceToken)} is not valid");
            }

            var now = DateTime.UtcNow;
            // TODO: generation and conversion could be omitted and left to mongodb to handle the _id
            var key = ObjectId.GenerateNewId(now).ToString();

            BsonDocument subscription = GetSubscriptionInfo(pushContactModel);

            var pushContactDocument = new BsonDocument {
                { PushContactDocumentProps.IdPropName, key },
                { PushContactDocumentProps.DomainPropName, pushContactModel.Domain },
                { PushContactDocumentProps.DeviceTokenPropName, pushContactModel.DeviceToken },
                { PushContactDocumentProps.EmailPropName, string.IsNullOrEmpty(pushContactModel.Email) ? BsonNull.Value : pushContactModel.Email },
                { PushContactDocumentProps.VisitorGuidPropName, string.IsNullOrEmpty(pushContactModel.VisitorGuid) ? BsonNull.Value : pushContactModel.VisitorGuid},
                { PushContactDocumentProps.DeletedPropName, false },
                { PushContactDocumentProps.ModifiedPropName, now },
                { PushContactDocumentProps.Subscription_PropName, subscription == null ? BsonNull.Value : subscription },
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

        public async Task<bool> UpdateSubscriptionAsync(string deviceToken, SubscriptionModel subscription)
        {
            if (string.IsNullOrEmpty(deviceToken) || string.IsNullOrWhiteSpace(deviceToken))
            {
                throw new ArgumentException($"'{nameof(deviceToken)}' cannot be null, empty or whitespace.");
            }

            if (subscription == null)
            {
                throw new ArgumentException($"'{nameof(subscription)}' cannot be null.");
            }

            if (string.IsNullOrEmpty(subscription.EndPoint) || string.IsNullOrEmpty(subscription.Keys.P256DH) || string.IsNullOrEmpty(subscription.Keys.Auth))
            {
                throw new ArgumentException($"'{nameof(subscription)}' fields cannot be null, empty or whitespace.");
            }

            if (!Uri.IsWellFormedUriString(subscription.EndPoint, UriKind.Absolute))
            {
                throw new ArgumentException($"'{nameof(subscription)}' pass a subscription with at least a valid endpoint.");
            }

            BsonDocument subscriptionDocument = new BsonDocument {
                { PushContactDocumentProps.Subscription_EndPoint_PropName, subscription.EndPoint },
                { PushContactDocumentProps.Subscription_P256DH_PropName, subscription.Keys.P256DH },
                { PushContactDocumentProps.Subscription_Auth_PropName, subscription.Keys.Auth },
            };

            var updateFilter = Builders<BsonDocument>.Filter.Eq(PushContactDocumentProps.DeviceTokenPropName, deviceToken)
                & Builders<BsonDocument>.Filter.Eq(PushContactDocumentProps.DeletedPropName, false);

            var updateDefinition = Builders<BsonDocument>.Update
                .Set(PushContactDocumentProps.Subscription_PropName, subscriptionDocument)
                .Set(PushContactDocumentProps.ModifiedPropName, DateTime.UtcNow);

            try
            {
                var result = await PushContacts.UpdateOneAsync(updateFilter, updateDefinition);
                return result.ModifiedCount > 0;
            }
            catch (Exception ex)
            {
                var errorMessage = $@"Error updating {nameof(PushContactModel)} with {nameof(deviceToken)} {deviceToken}." +
                    $" The {PushContactDocumentProps.Subscription_PropName} can not be updated with following value: {subscriptionDocument}";
                _logger.LogError(ex, errorMessage);

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

                    var update = Builders<BsonDocument>.Update
                    .Push(PushContactDocumentProps.HistoryEventsPropName, historyEvent)
                    .Set(PushContactDocumentProps.ModifiedPropName, DateTime.UtcNow);

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

        public async Task AddHistoryEventsAsync(Guid messageId, SendMessageResult sendMessageResult)
        {
            //TO DO: implement abstraction
            if (sendMessageResult == null)
            {
                throw new ArgumentNullException($"{typeof(SendMessageResult)} cannot be null");
            }

            var notValidTargetDeviceToken = sendMessageResult
            .SendMessageTargetResult?
            .Where(x => !x.IsValidTargetDeviceToken)
            .Select(x => x.TargetDeviceToken);

            if (notValidTargetDeviceToken != null && notValidTargetDeviceToken.Any())
            {
                await DeleteByDeviceTokenAsync(notValidTargetDeviceToken);
            }

            var now = DateTime.UtcNow;

            var pushContactHistoryEvents = sendMessageResult
                .SendMessageTargetResult?
                .Select(x => new PushContactHistoryEvent
                {
                    DeviceToken = x.TargetDeviceToken,
                    SentSuccess = x.IsSuccess,
                    EventDate = now,
                    Details = x.NotSuccessErrorDetails,
                    MessageId = messageId
                });

            if (pushContactHistoryEvents != null && pushContactHistoryEvents.Any())
            {
                await AddHistoryEventsAsync(pushContactHistoryEvents);
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

        private List<SubscriptionInfoDTO> GetSubscriptionsInfoFromBsonDocuments(List<BsonDocument> pushContacts)
        {
            return pushContacts.Select(x =>
            {
                var DEVTOKEN_PROP_NAME = PushContactDocumentProps.DeviceTokenPropName;
                var SUBSCRIPTION_PROP_NAME = PushContactDocumentProps.Subscription_PropName;
                var ENDPOINT_PROP_NAME = PushContactDocumentProps.Subscription_EndPoint_PropName;
                var AUTH_PROP_NAME = PushContactDocumentProps.Subscription_Auth_PropName;
                var P256DH_PROP_NAME = PushContactDocumentProps.Subscription_P256DH_PropName;
                var _ID_PROP_NAME = PushContactDocumentProps.IdPropName;

                var deviceToken = x.Contains(DEVTOKEN_PROP_NAME) && !x[DEVTOKEN_PROP_NAME].IsBsonNull
                    ? x[DEVTOKEN_PROP_NAME].AsString
                    : null;

                var _id = x[_ID_PROP_NAME].AsString;

                if (x.Contains(SUBSCRIPTION_PROP_NAME) && !x[SUBSCRIPTION_PROP_NAME].IsBsonNull)
                {
                    var subscriptionDoc = x[SUBSCRIPTION_PROP_NAME].AsBsonDocument;

                    var endPoint = subscriptionDoc.Contains(ENDPOINT_PROP_NAME) && !subscriptionDoc[ENDPOINT_PROP_NAME].IsBsonNull
                        ? subscriptionDoc[ENDPOINT_PROP_NAME].AsString
                        : null;

                    var auth = subscriptionDoc.Contains(AUTH_PROP_NAME) && !subscriptionDoc[AUTH_PROP_NAME].IsBsonNull
                        ? subscriptionDoc[AUTH_PROP_NAME].AsString
                        : null;

                    var p256dh = subscriptionDoc.Contains(P256DH_PROP_NAME) && !subscriptionDoc[P256DH_PROP_NAME].IsBsonNull
                        ? subscriptionDoc[P256DH_PROP_NAME].AsString
                        : null;

                    var subscriptionModel = new SubscriptionModel
                    {
                        EndPoint = endPoint,
                        Keys = new SubscriptionKeys
                        {
                            Auth = auth,
                            P256DH = p256dh
                        }
                    };

                    return new SubscriptionInfoDTO
                    {
                        DeviceToken = deviceToken,
                        Subscription = subscriptionModel,
                        PushContactId = _id,
                    };
                }
                else
                {
                    return new SubscriptionInfoDTO
                    {
                        DeviceToken = deviceToken,
                        Subscription = null,
                        PushContactId = _id,
                    };
                }
            }).ToList();
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

        public async Task<IEnumerable<string>> GetAllDeviceTokensByVisitorGuidAsync(string visitorGuid)
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
                _logger.LogError(ex, $"Error getting {nameof(PushContactModel)}s by {nameof(visitorGuid)} {visitorGuid}");

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

        public async Task<MessageDeliveryResult> GetDeliveredMessageSummarizationAsync(string domain, Guid messageId, DateTimeOffset from, DateTimeOffset to)
        {
            var historyEventsMessageIdFieldName = $"{PushContactDocumentProps.HistoryEventsPropName}.{PushContactDocumentProps.HistoryEvents_MessageIdPropName}";
            var historyEventsSentSuccessFieldName = $"{PushContactDocumentProps.HistoryEventsPropName}.{PushContactDocumentProps.HistoryEvents_SentSuccessPropName}";
            var historyEventsInsertedDateFieldName = $"{PushContactDocumentProps.HistoryEventsPropName}.{PushContactDocumentProps.HistoryEvents_InsertedDatePropName}";

            BsonBinaryData messageIdFormatted = messageIdFormatted = new BsonBinaryData(messageId, GuidRepresentation.Standard);

            try
            {
                var historyEventsResultFiltered = await PushContacts.Aggregate()
                    .Match(new BsonDocument
                    {
                        { $"{PushContactDocumentProps.DomainPropName}", domain },
                        { historyEventsMessageIdFieldName, messageIdFormatted },
                        { historyEventsInsertedDateFieldName, new BsonDocument
                        {
                            { "$gte", new BsonDateTime(from.ToUnixTimeMilliseconds()) },
                            { "$lt", new BsonDateTime(to.ToUnixTimeMilliseconds()) },
                        }}
                    })
                    .Unwind($"{PushContactDocumentProps.HistoryEventsPropName}")
                    .Match(new BsonDocument
                    {
                        { historyEventsMessageIdFieldName, messageIdFormatted }
                    })
                    .Project(new BsonDocument
                    {
                        { $"{PushContactDocumentProps.IdPropName}", 0 },
                        { "Pos",
                                new BsonDocument("$cond",
                                    new BsonArray
                                    {
                                        new BsonDocument("$eq",
                                            new BsonArray
                                            {
                                                "$" + historyEventsSentSuccessFieldName,
                                                true
                                            }),
                                            1,
                                            0
                        })},
                        { "Neg",
                                new BsonDocument("$cond",
                                    new BsonArray
                                    {
                                        new BsonDocument("$ne",
                                            new BsonArray
                                            {
                                                "$" + historyEventsSentSuccessFieldName,
                                                true
                                            }),
                                            1,
                                            0
                        })},
                    })
                    .Group(new BsonDocument
                    {
                        { $"{PushContactDocumentProps.IdPropName}", BsonNull.Value },
                        { "delivered", new BsonDocument
                            {
                                { "$sum", "$Pos" } ,
                            }
                        },
                        { "notDelivered", new BsonDocument
                            {
                                { "$sum", "$Neg" } ,
                            }
                        }
                    })
                    .ToListAsync();

                int delivered;
                int notDelivered;
                int sent;

                if (historyEventsResultFiltered.Any())
                {
                    delivered = historyEventsResultFiltered.FirstOrDefault().GetValue("delivered", 0).AsInt32;
                    notDelivered = historyEventsResultFiltered.FirstOrDefault().GetValue("notDelivered", 0).AsInt32;
                    sent = delivered + notDelivered;
                }
                else
                {
                    delivered = 0;
                    notDelivered = 0;
                    sent = 0;
                }

                return new MessageDeliveryResult { Domain = domain, Delivered = delivered, NotDelivered = notDelivered, SentQuantity = sent };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error summarizing messages from {nameof(PushContactHistoryEvent)}s by {nameof(messageId)} {messageId}");

                throw;
            }
        }

        public async Task UpdatePushContactVisitorGuid(string deviceToken, string visitorGuid)
        {
            try
            {
                var filterBuilder = Builders<BsonDocument>.Filter;

                var filter = filterBuilder.Eq(PushContactDocumentProps.DeviceTokenPropName, deviceToken)
                    & filterBuilder.Eq(PushContactDocumentProps.DeletedPropName, false);

                var updateDefinition = Builders<BsonDocument>.Update
                .Set(PushContactDocumentProps.VisitorGuidPropName, visitorGuid);

                await PushContacts.UpdateOneAsync(filter, updateDefinition);
            }
            catch (Exception ex)
            {
                throw new Exception($"An error occurred updating visitor-guid: {visitorGuid} for the push contact with the {nameof(deviceToken)} {deviceToken}", ex);
            }
        }

        public async Task<ApiPage<string>> GetAllVisitorGuidByDomain(string domain, int page, int per_page)
        {
            var filterBuilder = Builders<BsonDocument>.Filter;

            var filter = filterBuilder.Eq(PushContactDocumentProps.DomainPropName, domain)
                & filterBuilder.Eq(PushContactDocumentProps.DeletedPropName, false)
                & filterBuilder.Ne(PushContactDocumentProps.VisitorGuidPropName, (string)null)
                & filterBuilder.Exists(PushContactDocumentProps.VisitorGuidPropName);

            var options = new FindOptions<BsonDocument>
            {
                Projection = Builders<BsonDocument>.Projection
                .Include(PushContactDocumentProps.VisitorGuidPropName)
                .Exclude(PushContactDocumentProps.IdPropName),
                Skip = page,
                Limit = per_page
            };

            try
            {
                var pushContactsFiltered = await (await PushContacts.FindAsync(filter, options)).ToListAsync();
                var visitorGuids = pushContactsFiltered.Select(x => x.GetValue(PushContactDocumentProps.VisitorGuidPropName).AsString).ToList();
                var newPage = page + pushContactsFiltered.Count;

                return new ApiPage<string>(visitorGuids, newPage, per_page);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting {nameof(PushContactModel)}s by {nameof(domain)} {domain}");

                throw new Exception($"Error getting {nameof(PushContactModel)}s by {nameof(domain)} {domain}", ex);
            }
        }

        public async Task<bool> GetEnabledByVisitorGuid(string domain, string visitorGuid)
        {
            var filterBuilder = Builders<BsonDocument>.Filter;
            var filter = filterBuilder.Eq(PushContactDocumentProps.DomainPropName, domain)
                & filterBuilder.Eq(PushContactDocumentProps.VisitorGuidPropName, visitorGuid)
                & filterBuilder.Eq(PushContactDocumentProps.DeletedPropName, false);

            try
            {
                var pushContactsFiltered = await (await PushContacts.FindAsync(filter)).ToListAsync();

                return pushContactsFiltered.Any();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting active {nameof(PushContactModel)}s by {nameof(visitorGuid)} {visitorGuid}");

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
