using MongoDB.Bson.Serialization.Attributes;

namespace Doppler.PushContact.Models.Entities
{
    public class MessageAction
    {
        [BsonElement(MessageDocumentProps.Actions_ActionPropName)]
        public string Action { get; set; }

        [BsonElement(MessageDocumentProps.Actions_TitlePropName)]
        public string Title { get; set; }

        [BsonElement(MessageDocumentProps.Actions_IconPropName)]
        public string Icon { get; set; }

        [BsonElement(MessageDocumentProps.Actions_LinkPropName)]
        public string Link { get; set; }
    }
}
