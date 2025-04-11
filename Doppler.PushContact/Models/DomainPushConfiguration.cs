namespace Doppler.PushContact.Models
{
    public class DomainPushConfiguration
    {
        public bool IsPushFeatureEnabled { get; set; }
        public bool UsesExternalPushDomain { get; set; }
        public string ExternalPushDomain { get; set; }
    }
}
