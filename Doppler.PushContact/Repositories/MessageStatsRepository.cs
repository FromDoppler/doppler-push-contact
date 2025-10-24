using Doppler.PushContact.Models.Entities;
using Doppler.PushContact.Repositories.Interfaces;
using Doppler.PushContact.Services;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Doppler.PushContact.Repositories
{
    public class MessageStatsRepository : IMessageStatsRepository
    {
        private readonly IMongoClient _mongoClient;
        private readonly IOptions<PushMongoContextSettings> _pushMongoContextSettings;

        public MessageStatsRepository(
            IMongoClient mongoClient,
            IOptions<PushMongoContextSettings> pushMongoContextSettings
        )
        {
            _mongoClient = mongoClient;
            _pushMongoContextSettings = pushMongoContextSettings;
        }

        public async Task BulkUpsertStatsAsync(IEnumerable<MessageStats> stats)
        {
            // generate each update query to do a bulk upsert
            var updates = stats.Select(stat =>
            {
                var filter = Builders<MessageStats>.Filter.And(
                    Builders<MessageStats>.Filter.Eq(s => s.Domain, stat.Domain),
                    Builders<MessageStats>.Filter.Eq(s => s.MessageId, stat.MessageId),
                    Builders<MessageStats>.Filter.Eq(s => s.Date, stat.Date)
                );

                var update = Builders<MessageStats>.Update
                    .Inc(s => s.Sent, stat.Sent)
                    .Inc(s => s.Delivered, stat.Delivered)
                    .Inc(s => s.NotDelivered, stat.NotDelivered)
                    .Inc(s => s.Received, stat.Received)
                    .Inc(s => s.Click, stat.Click)
                    .Inc(s => s.BillableSends, stat.BillableSends)
                    .Inc(s => s.ActionClick, stat.ActionClick);

                return new UpdateOneModel<MessageStats>(filter, update) { IsUpsert = true };
            });

            await MessageStats.BulkWriteAsync(updates);
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

            await MessageStats.UpdateOneAsync(filter, upsertDefinition, new UpdateOptions { IsUpsert = true });
        }

        private IMongoCollection<MessageStats> MessageStats
        {
            get
            {
                var database = _mongoClient.GetDatabase(_pushMongoContextSettings.Value.DatabaseName);
                return database.GetCollection<MessageStats>(_pushMongoContextSettings.Value.MessageStatsCollectionName);
            }
        }
    }
}
