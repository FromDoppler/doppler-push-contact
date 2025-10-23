using Doppler.PushContact.Models.Entities;
using Doppler.PushContact.WebPushSender.Repositories.Interfaces;
using Doppler.PushContact.WebPushSender.Repositories.Setup;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using System;
using System.Threading.Tasks;

namespace Doppler.PushContact.Repositories
{
    public class MessageStatsRepository : IMessageStatsRepository
    {
        private readonly IMongoCollection<MessageStats> _collection;

        public MessageStatsRepository(IMongoDatabase database, IOptions<RepositorySettings> repositorySettings)
        {
            _collection = database.GetCollection<MessageStats>(repositorySettings.Value.MessageStatsCollectionName);
        }

        public async Task UpsertMessageStatsAsync(MessageStats messageStats)
        {
            if (messageStats == null)
            {
                throw new ArgumentNullException(nameof(messageStats));
            }

            var filter = Builders<MessageStats>.Filter.And(
                Builders<MessageStats>.Filter.Eq(s => s.Domain, messageStats.Domain),
                Builders<MessageStats>.Filter.Eq(s => s.MessageId, messageStats.MessageId),
                Builders<MessageStats>.Filter.Eq(s => s.Date, messageStats.Date)
            );

            var upsertDefinition = Builders<MessageStats>.Update
                .Inc(s => s.Sent, messageStats.Sent)
                .Inc(s => s.Delivered, messageStats.Delivered)
                .Inc(s => s.NotDelivered, messageStats.NotDelivered)
                .Inc(s => s.Received, messageStats.Received)
                .Inc(s => s.Click, messageStats.Click)
                .Inc(s => s.BillableSends, messageStats.BillableSends)
                .Inc(s => s.ActionClick, messageStats.ActionClick);

            await _collection.UpdateOneAsync(filter, upsertDefinition, new UpdateOptions { IsUpsert = true });
        }
    }
}
