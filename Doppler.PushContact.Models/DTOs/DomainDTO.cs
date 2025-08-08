namespace Doppler.PushContact.Models.DTOs
{
    public class DomainDTO
    {
        public string Name { get; set; }
        public bool IsPushFeatureEnabled { get; set; }
        public bool UsesExternalPushDomain { get; set; }
        public string ExternalPushDomain { get; set; }
    }
}
