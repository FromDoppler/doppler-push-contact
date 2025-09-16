using Doppler.PushContact.Models.Entities;
using Doppler.PushContact.Models.Enums;
using Doppler.PushContact.WebPushSender.Repositories.Interfaces;
using Doppler.PushContact.WebPushSender.Repositories.Setup;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using System;
using System.Threading.Tasks;

namespace Doppler.PushContact.WebPushSender.Repositories
{
    public class MessageRepository : IMessageRepository
    {
        private readonly IMongoCollection<BsonDocument> _collection;
        private readonly ILogger<MessageRepository> _logger;

        public MessageRepository(
            IMongoDatabase database,
            IOptions<RepositorySettings> repositorySettings,
            ILogger<MessageRepository> logger
        )
        {
            _collection = database.GetCollection<BsonDocument>(repositorySettings.Value.MessageCollectionName);
            _logger = logger;
        }

        public async Task RegisterStatisticsAsync(Guid messageId, WebPushEvent webPushEvent)
        {
            var sent = 1;
            var delivered = webPushEvent.Type == (int)WebPushEventType.Delivered ? 1 : 0;

            // TODO: consider re-analyze summarization when ProcessingFailed and DeliveryFailedButRetry will be treated
            var notDelivered =
                webPushEvent.Type == (int)WebPushEventType.ProcessingFailed ||
                webPushEvent.Type == (int)WebPushEventType.DeliveryFailed ||
                webPushEvent.Type == (int)WebPushEventType.DeliveryFailedButRetry ? 1 : 0;

            await UpdateDeliveriesSafe(messageId, sent, delivered, notDelivered);
        }

        private async Task UpdateDeliveriesSafe(Guid messageId, int sent, int delivered, int notDelivered)
        {
            var filterDefinition = Builders<BsonDocument>.Filter
                .Eq(MessageDocumentProps.MessageIdPropName, new BsonBinaryData(messageId, GuidRepresentation.Standard));

            var updateDefinition = Builders<BsonDocument>.Update
                .Inc(MessageDocumentProps.SentPropName, sent)
                .Inc(MessageDocumentProps.DeliveredPropName, delivered)
                .Inc(MessageDocumentProps.NotDeliveredPropName, notDelivered);

            try
            {
                await _collection.UpdateOneAsync(filterDefinition, updateDefinition);
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"Error updating message counters with {nameof(messageId)} {messageId}");
            }
        }
    }
}
