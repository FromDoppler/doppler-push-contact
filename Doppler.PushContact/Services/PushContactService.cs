using Doppler.PushContact.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using System;
using System.Threading.Tasks;

namespace Doppler.PushContact.Services
{
    public class PushContactService : IPushContactService
    {
        private readonly IMongoClient _mongoClient;
        private readonly IOptions<PushContactMongoContextSettings> _pushContactMongoContextSettings;
        private readonly ILogger<PushContactService> _logger;

        public PushContactService(
            IMongoClient mongoClient,
            IOptions<PushContactMongoContextSettings> pushContactMongoContextSettings,
            ILogger<PushContactService> logger)
        {

            _mongoClient = mongoClient;
            _pushContactMongoContextSettings = pushContactMongoContextSettings;
            _logger = logger;
        }

        public async Task<bool> AddAsync(PushContactModel pushContactModel)
        {
            if (pushContactModel == null)
            {
                throw new ArgumentNullException(nameof(pushContactModel));
            }

            var now = DateTime.UtcNow;
            var key = ObjectId.GenerateNewId(now).ToString();

            var pushContactDocument = new BsonDocument {
                { "_id", key },
                { "domain", pushContactModel.Domain },
                { "device_token", pushContactModel.DeviceToken },
                { "modified", now }
            };

            try
            {
                await PushContacts.InsertOneAsync(pushContactDocument);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, @$"Error inserting {nameof(pushContactModel)}
with following {nameof(pushContactModel.DeviceToken)}: {pushContactModel.DeviceToken}");

                return false;
            }

            return true;
        }

        private IMongoCollection<BsonDocument> PushContacts
        {
            get
            {
                var database = _mongoClient.GetDatabase(_pushContactMongoContextSettings.Value.MongoPushContactDatabaseName);
                return database.GetCollection<BsonDocument>(_pushContactMongoContextSettings.Value.MongoPushContactCollectionName);
            }
        }
    }
}
