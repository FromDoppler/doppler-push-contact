namespace Doppler.PushContact.Services
{
    public class PushMongoContextSettings
    {
        public string ConnectionString { get; set; }

        public string Password { get; set; }

        public string DatabaseName { get; set; }

        public string PushContactsCollectionName { get; set; }

        public string DomainsCollectionName { get; set; }

        public string MessagesCollectionName { get; set; }
        public string WebPushEventCollectionName { get; set; }
        public int CursorBatchSize { get; set; }
        public string MessageStatsCollectionName { get; set; }
    }
}
