namespace Doppler.PushContact.WebPushSender.Repositories.Setup
{
    public class RepositorySettings
    {
        public string ConnectionUrl { get; set; }

        /// <summary>
        /// Default database name to use when is not specified in ConnectionUrl
        /// </summary>
        public string DatabaseName { get; set; }

        /// <summary>
        /// Secret password to use when is not specified in ConnectionUrl
        /// </summary>
        public string SecretPassword { get; set; }

        public string WebPushEventCollectionName { get; set; }

        public string PushContactsCollectionName { get; set; }
        public string MessageCollectionName { get; set; }
        public string MessageStatsCollectionName { get; set; }
    }
}
