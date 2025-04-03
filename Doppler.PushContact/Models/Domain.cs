namespace Doppler.PushContact.Models
{
    public class Domain
    {
        public string Name { get; set; }

        public bool IsPushFeatureEnabled { get; set; }
        public bool UsesExternalPushDomain { get; set; }
        public string ExternalPushDomain { get; set; }
    }
}
