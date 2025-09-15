using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;

namespace Doppler.PushContact.Models.Entities
{
    public class WebPushEvent
    {
        [BsonElement(WebPushEventDocumentProps.Type_PropName)]
        public int Type { get; set; }

        [BsonElement(WebPushEventDocumentProps.Date_PropName)]
        public DateTime Date { get; set; }

        [BsonElement(WebPushEventDocumentProps.MessageId_PropName)]
        public Guid MessageId { get; set; }

        [BsonElement(WebPushEventDocumentProps.PushContactId_PropName)]
        public string PushContactId { get; set; }

        [BsonElement(WebPushEventDocumentProps.ErrorMessage_PropName)]
        public string ErrorMessage { get; set; }

        [BsonElement(WebPushEventDocumentProps.SubType_PropName)]
        public int SubType { get; set; }

        [BsonElement(WebPushEventDocumentProps.Domain_PropName)]
        public string Domain { get; set; }

        [BsonElement(WebPushEventDocumentProps.DeviceToken_PropName)]
        public string DeviceToken { get; set; }
    }
}
