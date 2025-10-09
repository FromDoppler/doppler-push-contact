using Doppler.PushContact.ApiModels;
using Doppler.PushContact.Models.DTOs;
using Doppler.PushContact.Models.Entities;
using Doppler.PushContact.Models.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Doppler.PushContact.Services.Messages
{
    public class MessageRepository : IMessageRepository
    {
        private readonly IMongoClient _mongoClient;
        private readonly IOptions<PushMongoContextSettings> _pushMongoContextSettings;
        private readonly ILogger<MessageRepository> _logger;

        public MessageRepository(
            IMongoClient mongoClient,
            IOptions<PushMongoContextSettings> pushMongoContextSettings,
            ILogger<MessageRepository> logger)
        {

            _mongoClient = mongoClient;
            _pushMongoContextSettings = pushMongoContextSettings;
            _logger = logger;
        }

        public async Task AddAsync(
            Guid messageId,
            string domain,
            string title,
            string body,
            string onClickLink,
            int sent,
            int delivered,
            int notDelivered,
            string imageUrl,
            List<MessageActionDTO> actions = null
        )
        {
            if (string.IsNullOrEmpty(domain))
            {
                throw new ArgumentException($"'{nameof(domain)}' cannot be null or empty.", nameof(domain));
            }

            if (string.IsNullOrEmpty(title))
            {
                throw new ArgumentException($"'{nameof(title)}' cannot be null or empty.", nameof(title));
            }

            if (string.IsNullOrEmpty(body))
            {
                throw new ArgumentException($"'{nameof(body)}' cannot be null or empty.", nameof(body));
            }

            var now = DateTime.UtcNow;
            // TODO: review it. The _id property should be handled automatically by mongodb.
            var key = ObjectId.GenerateNewId(now).ToString();

            var messageDocument = new BsonDocument {
                { MessageDocumentProps.IdPropName, key },
                { MessageDocumentProps.MessageIdPropName, new BsonBinaryData(messageId, GuidRepresentation.Standard) },
                { MessageDocumentProps.DomainPropName, domain },
                { MessageDocumentProps.TitlePropName, title },
                { MessageDocumentProps.BodyPropName, body },
                { MessageDocumentProps.OnClickLinkPropName, string.IsNullOrEmpty(onClickLink) ? BsonNull.Value : onClickLink },
                { MessageDocumentProps.SentPropName, sent },
                { MessageDocumentProps.DeliveredPropName, delivered },
                { MessageDocumentProps.NotDeliveredPropName, notDelivered },
                { MessageDocumentProps.BillableSendsPropName, 0 },
                { MessageDocumentProps.ReceivedPropName, 0 },
                { MessageDocumentProps.ClicksPropName, 0 },
                { MessageDocumentProps.ImageUrlPropName, string.IsNullOrEmpty(imageUrl) ? BsonNull.Value : imageUrl},
                { MessageDocumentProps.InsertedDatePropName, now }
            };

            // only add "actions" property when it has some action defined
            if (actions != null && actions.Any())
            {
                var bsonActions = MapActions(actions);
                messageDocument.Add(MessageDocumentProps.ActionsPropName, bsonActions);
            }

            try
            {
                await Messages.InsertOneAsync(messageDocument);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, @$"Error inserting message with {nameof(messageId)} {messageId}");

                throw;
            }
        }

        private BsonArray MapActions(List<MessageActionDTO> actions)
        {
            var bsonActions = new BsonArray();

            foreach (var actionDto in actions)
            {
                var bsonAction = new BsonDocument
                    {
                        { MessageDocumentProps.Actions_ActionPropName, actionDto.Action },
                        { MessageDocumentProps.Actions_TitlePropName, actionDto.Title },
                        { MessageDocumentProps.Actions_IconPropName, string.IsNullOrEmpty(actionDto.Icon) ? BsonNull.Value : actionDto.Icon },
                        { MessageDocumentProps.Actions_LinkPropName, string.IsNullOrEmpty(actionDto.Link) ? BsonNull.Value : actionDto.Link },
                    };

                bsonActions.Add(bsonAction);
            }

            return bsonActions;
        }

        public async Task RegisterStatisticsAsync(Guid messageId, IEnumerable<WebPushEvent> webPushEvents)
        {
            if (webPushEvents == null || !webPushEvents.Any())
            {
                return;
            }

            var sent = webPushEvents.Count();
            var delivered = webPushEvents.Count(x => x.Type == (int)WebPushEventType.Delivered);
            var notDelivered = sent - delivered;

            var billableSends = webPushEvents.Count(x =>
                x.Type == (int)WebPushEventType.Delivered ||
                (x.Type == (int)WebPushEventType.DeliveryFailed && x.SubType == (int)WebPushEventSubType.InvalidSubcription)
            );

            await UpdateDeliveriesAsync(messageId, sent, delivered, notDelivered, billableSends);
        }

        // TODO: redefine as private when the endpoint accessing this is removed (maybe rename to UpdateDeliveriesSafe)
        public async Task UpdateDeliveriesAsync(Guid messageId, int sent, int delivered, int notDelivered, int billableSends = 0)
        {
            var filterDefinition = Builders<BsonDocument>.Filter
                .Eq(MessageDocumentProps.MessageIdPropName, new BsonBinaryData(messageId, GuidRepresentation.Standard));

            var updateDefinition = Builders<BsonDocument>.Update
                .Inc(MessageDocumentProps.SentPropName, sent)
                .Inc(MessageDocumentProps.DeliveredPropName, delivered)
                .Inc(MessageDocumentProps.NotDeliveredPropName, notDelivered)
                .Inc(MessageDocumentProps.BillableSendsPropName, billableSends);

            try
            {
                await Messages.UpdateOneAsync(filterDefinition, updateDefinition);
            }
            catch (Exception e)
            {
                _logger.LogError(e, @$"Error updating message counters with {nameof(messageId)} {messageId}");
            }
        }

        public async Task RegisterEventCount(Guid messageId, WebPushEvent webPushEvent)
        {
            var filterDefinition = Builders<BsonDocument>.Filter
                .Eq(MessageDocumentProps.MessageIdPropName, new BsonBinaryData(messageId, GuidRepresentation.Standard));

            var quantity = 1;
            UpdateDefinition<BsonDocument> updateDefinition = null;
            switch (webPushEvent.Type)
            {
                case (int)WebPushEventType.Delivered: // register for sent and billable
                    updateDefinition = Builders<BsonDocument>.Update
                        .Inc(MessageDocumentProps.DeliveredPropName, quantity)
                        .Inc(MessageDocumentProps.SentPropName, quantity)
                        .Inc(MessageDocumentProps.BillableSendsPropName, quantity);
                    break;
                case (int)WebPushEventType.DeliveryFailed: // register for sent
                    updateDefinition = Builders<BsonDocument>.Update
                        .Inc(MessageDocumentProps.NotDeliveredPropName, quantity)
                        .Inc(MessageDocumentProps.SentPropName, quantity);

                    // when InvalidSubcription register for billable too
                    if (webPushEvent.SubType == (int)WebPushEventSubType.InvalidSubcription)
                    {
                        updateDefinition = updateDefinition.Inc(MessageDocumentProps.BillableSendsPropName, quantity);
                    }
                    break;
                case (int)WebPushEventType.ProcessingFailed: // register for sent
                    updateDefinition = Builders<BsonDocument>.Update
                        .Inc(MessageDocumentProps.NotDeliveredPropName, quantity)
                        .Inc(MessageDocumentProps.SentPropName, quantity);
                    break;
                case (int)WebPushEventType.DeliveryFailedButRetry: // register for sent
                    updateDefinition = Builders<BsonDocument>.Update
                        .Inc(MessageDocumentProps.NotDeliveredPropName, quantity)
                        .Inc(MessageDocumentProps.SentPropName, quantity);
                    break;
                case (int)WebPushEventType.Received:
                    updateDefinition = Builders<BsonDocument>.Update
                        .Inc(MessageDocumentProps.ReceivedPropName, quantity);
                    break;
                case (int)WebPushEventType.Clicked:
                    updateDefinition = Builders<BsonDocument>.Update
                        .Inc(MessageDocumentProps.ClicksPropName, quantity);
                    break;
                default:
                    _logger.LogError($"Event type being registered is not valid for message with {nameof(messageId)} {messageId}");
                    break;
            }

            try
            {
                if (updateDefinition != null)
                {
                    await Messages.UpdateOneAsync(filterDefinition, updateDefinition);
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"Error registering click/receive event in message with {nameof(messageId)} {messageId}");
            }
        }

        public async Task<MessageDetails> GetMessageDetailsAsync(string domain, Guid messageId, DateTimeOffset? dateFrom = null, DateTimeOffset? dateTo = null)
        {
            var filterBuilder = Builders<BsonDocument>.Filter;
            var filter = filterBuilder.Eq(MessageDocumentProps.DomainPropName, domain);
            filter &= filterBuilder.Eq(MessageDocumentProps.MessageIdPropName, new BsonBinaryData(messageId, GuidRepresentation.Standard));

            if (dateFrom.HasValue && dateTo.HasValue)
            {
                var from = new BsonDateTime(dateFrom.Value.UtcDateTime);
                var to = new BsonDateTime(dateTo.Value.UtcDateTime);
                filter &= filterBuilder.Gte(MessageDocumentProps.InsertedDatePropName, from) & filterBuilder.Lte(MessageDocumentProps.InsertedDatePropName, to);
            }

            try
            {
                BsonDocument message = await (await Messages.FindAsync<BsonDocument>(filter)).SingleOrDefaultAsync();

                if (message == null)
                {
                    return null;
                }

                var messageDetails = new MessageDetails
                {
                    MessageId = message.GetValue(MessageDocumentProps.MessageIdPropName).AsGuid,
                    Domain = message.GetValue(MessageDocumentProps.DomainPropName).AsString,
                    Title = message.GetValue(MessageDocumentProps.TitlePropName).AsString,
                    Body = message.GetValue(MessageDocumentProps.BodyPropName).AsString,
                    Sent = message.GetValue(MessageDocumentProps.SentPropName).AsInt32,
                    Delivered = message.GetValue(MessageDocumentProps.DeliveredPropName).AsInt32,
                    NotDelivered = message.GetValue(MessageDocumentProps.NotDeliveredPropName).AsInt32,
                    BillableSends = message.GetValue(MessageDocumentProps.BillableSendsPropName, 0).ToInt32(),
                    Clicks = message.GetValue(MessageDocumentProps.ClicksPropName, 0).ToInt32(),
                    Received = message.GetValue(MessageDocumentProps.ReceivedPropName, 0).ToInt32(),
                };

                if (message.TryGetValue(MessageDocumentProps.OnClickLinkPropName, out BsonValue onClickLinkValue))
                {
                    messageDetails.OnClickLink = onClickLinkValue == BsonNull.Value ? null : onClickLinkValue.AsString;
                }

                if (message.TryGetValue(MessageDocumentProps.ImageUrlPropName, out BsonValue imageUrlValue))
                {
                    messageDetails.ImageUrl = imageUrlValue == BsonNull.Value ? null : imageUrlValue.AsString;
                }

                return messageDetails;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting message with {nameof(domain)} {domain} and {nameof(messageId)} {messageId}");

                throw;
            }
        }

        public async Task<MessageDetails> GetMessageDetailsByMessageIdAsync(Guid messageId)
        {
            var filterBuilder = Builders<BsonDocument>.Filter;
            var filter = filterBuilder.Eq(MessageDocumentProps.MessageIdPropName, new BsonBinaryData(messageId, GuidRepresentation.Standard));

            try
            {
                var message = await (await Messages.FindAsync<BsonDocument>(filter)).SingleOrDefaultAsync();

                if (message == null)
                {
                    return null;
                }

                var messageDetails = new MessageDetails
                {
                    MessageId = message.GetValue(MessageDocumentProps.MessageIdPropName).AsGuid,
                    Domain = message.GetValue(MessageDocumentProps.DomainPropName).AsString,
                    Title = message.GetValue(MessageDocumentProps.TitlePropName).AsString,
                    Body = message.GetValue(MessageDocumentProps.BodyPropName).AsString,
                    OnClickLink = message.GetValue(MessageDocumentProps.OnClickLinkPropName) == BsonNull.Value
                        ? null
                        : message.GetValue(MessageDocumentProps.OnClickLinkPropName).AsString,
                    Sent = message.GetValue(MessageDocumentProps.SentPropName).AsInt32,
                    Delivered = message.GetValue(MessageDocumentProps.DeliveredPropName).AsInt32,
                    NotDelivered = message.GetValue(MessageDocumentProps.NotDeliveredPropName).AsInt32,
                    ImageUrl = message.GetValue(MessageDocumentProps.ImageUrlPropName) == BsonNull.Value
                        ? null
                        : message.GetValue(MessageDocumentProps.ImageUrlPropName).AsString,
                    BillableSends = message.GetValue(MessageDocumentProps.BillableSendsPropName, 0).ToInt32(),
                    Clicks = message.GetValue(MessageDocumentProps.ClicksPropName, 0).ToInt32(),
                    Received = message.GetValue(MessageDocumentProps.ReceivedPropName, 0).ToInt32(),
                };

                // Map actions (when exists)
                if (message.Contains(MessageDocumentProps.ActionsPropName) && message[MessageDocumentProps.ActionsPropName].IsBsonArray)
                {
                    var actionsArray = message[MessageDocumentProps.ActionsPropName].AsBsonArray;
                    var actions = new List<MessageAction>();

                    foreach (var bsonAction in actionsArray)
                    {
                        if (bsonAction.IsBsonDocument)
                        {
                            var doc = bsonAction.AsBsonDocument;
                            actions.Add(new MessageAction
                            {
                                Action = doc.GetValue(MessageDocumentProps.Actions_ActionPropName, BsonNull.Value).IsBsonNull ?
                                    null : doc[MessageDocumentProps.Actions_ActionPropName].AsString,
                                Title = doc.GetValue(MessageDocumentProps.Actions_TitlePropName, BsonNull.Value).IsBsonNull ?
                                    null : doc[MessageDocumentProps.Actions_TitlePropName].AsString,
                                Icon = doc.GetValue(MessageDocumentProps.Actions_IconPropName, BsonNull.Value).IsBsonNull ?
                                    null : doc[MessageDocumentProps.Actions_IconPropName].AsString,
                                Link = doc.GetValue(MessageDocumentProps.Actions_LinkPropName, BsonNull.Value).IsBsonNull ?
                                    null : doc[MessageDocumentProps.Actions_LinkPropName].AsString,
                            });
                        }
                    }

                    messageDetails.Actions = actions;
                }
                else
                {
                    messageDetails.Actions = new List<MessageAction>();
                }

                return messageDetails;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting message with {nameof(messageId)} {messageId}");
                throw;
            }
        }

        public async Task<ApiPage<MessageDeliveryResult>> GetMessages(int page, int per_page, DateTimeOffset from, DateTimeOffset to)
        {
            var filterBuilder = Builders<BsonDocument>.Filter;

            var filter = filterBuilder.Gte(MessageDocumentProps.InsertedDatePropName, from.UtcDateTime);

            filter &= filterBuilder.Lt(MessageDocumentProps.InsertedDatePropName, to.UtcDateTime);

            try
            {
                var messages = await Messages.Find(filter).Skip(page).Limit(per_page).ToListAsync();

                var list = messages
                    .Select(x => new MessageDeliveryResult()
                    {
                        Domain = x.GetValue(MessageDocumentProps.DomainPropName, null).AsString,
                        SentQuantity = x.GetValue(MessageDocumentProps.SentPropName, null).ToInt32(),
                        Delivered = x.GetValue(MessageDocumentProps.DeliveredPropName, null).ToInt32(),
                        NotDelivered = x.GetValue(MessageDocumentProps.NotDeliveredPropName, null).ToInt32(),
                        Date = x.GetValue(MessageDocumentProps.InsertedDatePropName, null).ToUniversalTime()
                    })
                    .ToList();

                var newPage = page + list.Count;

                return new ApiPage<MessageDeliveryResult>(list, newPage, per_page);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting messages from {from} to {to}");

                throw;
            }
        }

        public async Task IncrementMessageStats(Guid messageId, int sent, int delivered, int notDelivered)
        {
            var filter = Builders<BsonDocument>
                .Filter
                .Eq(MessageDocumentProps.MessageIdPropName, new BsonBinaryData(messageId, GuidRepresentation.Standard));

            var update = Builders<BsonDocument>.Update
                .Inc(MessageDocumentProps.SentPropName, sent)
                .Inc(MessageDocumentProps.DeliveredPropName, delivered)
                .Inc(MessageDocumentProps.NotDeliveredPropName, notDelivered);

            try
            {
                var result = await Messages.UpdateOneAsync(filter, update);
            }
            catch (Exception e)
            {
                _logger.LogError(e, @$"Error incrementing message stats for {nameof(messageId)} {messageId}");
                throw;
            }
        }

        public async Task<string> GetMessageDomainAsync(Guid messageId)
        {
            BsonBinaryData messageIdFormatted = new BsonBinaryData(messageId, GuidRepresentation.Standard);
            var filter = Builders<BsonDocument>.Filter.Eq(MessageDocumentProps.MessageIdPropName, messageIdFormatted);

            try
            {
                var message = await Messages.Find(filter).FirstOrDefaultAsync();
                return message != null ?
                    message.GetValue(MessageDocumentProps.DomainPropName, null)?.AsString : null;
            }
            catch (MongoException ex)
            {
                _logger.LogError(ex, $"MongoException getting Message by {nameof(messageId)} {messageId}");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Unexpected error getting Message by {nameof(messageId)} {messageId}");
                throw;
            }
        }

        public async Task<int> GetMessageSends(string domain, DateTimeOffset dateFrom, DateTimeOffset dateTo)
        {
            try
            {
                var filter = Builders<BsonDocument>.Filter.And(
                    Builders<BsonDocument>.Filter.Eq(MessageDocumentProps.DomainPropName, domain),
                    Builders<BsonDocument>.Filter.Gte(MessageDocumentProps.InsertedDatePropName, dateFrom.UtcDateTime),
                    Builders<BsonDocument>.Filter.Lte(MessageDocumentProps.InsertedDatePropName, dateTo.UtcDateTime)
                );

                var pipeline = Messages.Aggregate()
                    .Match(filter)
                    .Group(new BsonDocument
                    {
                        { "_id", BsonNull.Value },
                        { "Consumed", new BsonDocument("$sum", "$sent") }
                    });

                var result = await pipeline.FirstOrDefaultAsync();

                return result == null ? 0 : result["Consumed"].AsInt32;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error summarizing 'Messages' sends for domain: {domain}, from: {DateFrom}, to: {DateTo}.",
                    domain,
                    dateFrom.UtcDateTime,
                    dateTo.UtcDateTime
                );
                throw;
            }
        }

        private IMongoCollection<BsonDocument> Messages
        {
            get
            {
                var database = _mongoClient.GetDatabase(_pushMongoContextSettings.Value.DatabaseName);
                return database.GetCollection<BsonDocument>(_pushMongoContextSettings.Value.MessagesCollectionName);
            }
        }
    }
}
