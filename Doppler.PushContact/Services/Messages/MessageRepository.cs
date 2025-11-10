using Doppler.PushContact.ApiModels;
using Doppler.PushContact.Models.DTOs;
using Doppler.PushContact.Models.Entities;
using Doppler.PushContact.Models.Enums;
using Doppler.PushContact.Transversal;
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

        public async Task AddAsync(MessageDTO messageDTO)
        {
            var now = DateTime.UtcNow;
            // TODO: review it. The _id property should be handled automatically by mongodb.
            var key = ObjectId.GenerateNewId(now).ToString();

            var messageDocument = new BsonDocument {
                { MessageDocumentProps.IdPropName, key },
                { MessageDocumentProps.MessageIdPropName, new BsonBinaryData(messageDTO.MessageId, GuidRepresentation.Standard) },
                { MessageDocumentProps.DomainPropName, messageDTO.Domain },
                { MessageDocumentProps.TitlePropName, messageDTO.Title },
                { MessageDocumentProps.BodyPropName, messageDTO.Body },
                { MessageDocumentProps.OnClickLinkPropName, string.IsNullOrEmpty(messageDTO.OnClickLink) ? BsonNull.Value : messageDTO.OnClickLink },
                { MessageDocumentProps.SentPropName, 0 },
                { MessageDocumentProps.DeliveredPropName, 0 },
                { MessageDocumentProps.NotDeliveredPropName, 0 },
                { MessageDocumentProps.BillableSendsPropName, 0 },
                { MessageDocumentProps.ReceivedPropName, 0 },
                { MessageDocumentProps.ClicksPropName, 0 },
                { MessageDocumentProps.ImageUrlPropName, string.IsNullOrEmpty(messageDTO.ImageUrl) ? BsonNull.Value : messageDTO.ImageUrl },
                { MessageDocumentProps.PreferLargeImagePropName, messageDTO.PreferLargeImage },
                { MessageDocumentProps.IconUrlPropName, string.IsNullOrEmpty(messageDTO.IconUrl) ? BsonNull.Value : messageDTO.IconUrl },
                { MessageDocumentProps.InsertedDatePropName, now }
            };

            // only add "actions" property when it has some action defined
            if (messageDTO.Actions != null && messageDTO.Actions.Any())
            {
                var bsonActions = MapActions(messageDTO.Actions);
                messageDocument.Add(MessageDocumentProps.ActionsPropName, bsonActions);
            }

            try
            {
                await Messages.InsertOneAsync(messageDocument);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, @$"Error inserting message with {nameof(messageDTO.MessageId)} {messageDTO.MessageId}");

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

        public async Task RegisterShippingStatisticsAsync(Guid messageId, IEnumerable<WebPushEvent> webPushEvents)
        {
            if (webPushEvents == null || !webPushEvents.Any())
            {
                return;
            }

            var delivered = WebPushEventsHelper.GetDeliveredCount(webPushEvents);
            var notDelivered = WebPushEventsHelper.GetNotDeliveredCount(webPushEvents);
            var sent = delivered + notDelivered;
            var billableSends = WebPushEventsHelper.GetBillableSendsCount(webPushEvents);

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

        public async Task RegisterUserInteractionStats(Guid messageId, WebPushEvent webPushEvent)
        {
            var filterDefinition = Builders<BsonDocument>.Filter
                .Eq(MessageDocumentProps.MessageIdPropName, new BsonBinaryData(messageId, GuidRepresentation.Standard));

            var quantity = 1;
            UpdateDefinition<BsonDocument> updateDefinition = null;
            switch (webPushEvent.Type)
            {
                case (int)WebPushEventType.Received:
                    updateDefinition = Builders<BsonDocument>.Update
                        .Inc(MessageDocumentProps.ReceivedPropName, quantity);
                    break;
                case (int)WebPushEventType.Clicked:
                    updateDefinition = Builders<BsonDocument>.Update
                        .Inc(MessageDocumentProps.ClicksPropName, quantity);
                    break;
                case (int)WebPushEventType.ActionClick:
                    // TODO: sumarize actionClick in message
                    break;
                default:
                    _logger.LogWarning($"Event being registered doesn't correspond to user interaction for message with {nameof(messageId)} {messageId}");
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
                    PreferLargeImage = message.GetValue(MessageDocumentProps.PreferLargeImagePropName, false).AsBoolean,
                };

                // when icon_url is not present in document, it returns null (this validation is because icon_url could no exists in previous messages)
                var iconUrlValue = message.GetValue(MessageDocumentProps.IconUrlPropName, BsonNull.Value);
                messageDetails.IconUrl = iconUrlValue.IsBsonNull ? null : iconUrlValue.AsString;

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
