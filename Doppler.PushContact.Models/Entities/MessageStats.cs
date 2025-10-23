using MongoDB.Bson.Serialization.Attributes;
using System;

namespace Doppler.PushContact.Models.Entities
{
    public class MessageStats
    {
        [BsonElement(MessageStatsDocumentProps.Domain_PropName)]
        public string Domain { get; set; }

        [BsonElement(MessageStatsDocumentProps.MessageId_PropName)]
        public Guid MessageId { get; set; }

        [BsonElement(MessageStatsDocumentProps.Date_PropName)]
        public DateTime Date { get; set; }

        [BsonElement(MessageStatsDocumentProps.Sent_PropName)]
        public int Sent { get; set; }

        [BsonElement(MessageStatsDocumentProps.Delivered_PropName)]
        public int Delivered { get; set; }

        [BsonElement(MessageStatsDocumentProps.NotDelivered_PropName)]
        public int NotDelivered { get; set; }

        [BsonElement(MessageStatsDocumentProps.Received_PropName)]
        public int Received { get; set; }

        [BsonElement(MessageStatsDocumentProps.Click_PropName)]
        public int Click { get; set; }

        [BsonElement(MessageStatsDocumentProps.ActionClick_PropName)]
        public int ActionClick { get; set; }

        [BsonElement(MessageStatsDocumentProps.BillableSends_PropName)]
        public int BillableSends { get; set; }
    }
}
