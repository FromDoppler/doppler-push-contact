using System.Collections.Generic;

namespace Doppler.PushContact.Models.Models
{
    public class VisitorFields
    {
        public string VisitorGuid { get; set; }
        public Dictionary<string, string> Fields { get; set; }
        public bool ReplaceFields => Fields != null && Fields.Count > 0;
    }
}
