using Doppler.PushContact.Models.DTOs;
using Doppler.PushContact.Models.Entities;
using Doppler.PushContact.Repositories.Interfaces;
using Doppler.PushContact.Services;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
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

        public async Task<MessageStatsDTO> GetMessageStatsAsync(
            string domain,
            Guid? messageId,
            DateTimeOffset dateFrom,
            DateTimeOffset dateTo
        )
        {
            try
            {
                var from = new BsonDateTime(dateFrom.UtcDateTime);
                var to = new BsonDateTime(dateTo.UtcDateTime);

                var filter = new BsonDocument
                {
                    { MessageStatsDocumentProps.Date_PropName, new BsonDocument { { "$gte", from }, { "$lte", to } } }
                };

                if (!string.IsNullOrEmpty(domain))
                {
                    filter.Add(MessageStatsDocumentProps.Domain_PropName, domain);
                }

                if (messageId.HasValue && messageId.Value != Guid.Empty)
                {
                    filter.Add(MessageStatsDocumentProps.MessageId_PropName, new BsonBinaryData(messageId.Value, GuidRepresentation.Standard));
                }
                
                var pipeline = MessageStats.Aggregate()
                    .Match(filter)
                    .Group(new BsonDocument
                    {
                        { "_id", BsonNull.Value },
                        { "Sent", new BsonDocument("$sum", "$sent") },
                        { "Delivered", new BsonDocument("$sum", "$delivered") },
                        { "NotDelivered", new BsonDocument("$sum", "$not_delivered") },
                        { "Received", new BsonDocument("$sum", "$received") },
                        { "Click", new BsonDocument("$sum", "$click") },
                        { "ActionClick", new BsonDocument("$sum", "$action_click") },
                        { "BillableSends", new BsonDocument("$sum", "$billable_sends") }
                    })
                    .Project(new BsonDocument
                    {
                        { "_id", 0 },
                        { "Sent", 1 },
                        { "Delivered", 1 },
                        { "NotDelivered", 1 },
                        { "Received", 1 },
                        { "Click", 1 },
                        { "ActionClick", 1 },
                        { "BillableSends", 1 }
                    });

                var result = await pipeline.FirstOrDefaultAsync();

                return new MessageStatsDTO
                {
                    Domain = domain,
                    MessageId = messageId ?? Guid.Empty,
                    DateFrom = dateFrom,
                    DateTo = dateTo,
                    Sent = result?["Sent"]?.AsInt32 ?? 0,
                    Delivered = result?["Delivered"]?.AsInt32 ?? 0,
                    NotDelivered = result?["NotDelivered"]?.AsInt32 ?? 0,
                    Received = result?["Received"]?.AsInt32 ?? 0,
                    Click = result?["Click"]?.AsInt32 ?? 0,
                    ActionClick = result?["ActionClick"]?.AsInt32 ?? 0,
                    BillableSends = result?["BillableSends"]?.AsInt32 ?? 0
                };
            }
            catch (Exception)
            {
                throw;
            }
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
