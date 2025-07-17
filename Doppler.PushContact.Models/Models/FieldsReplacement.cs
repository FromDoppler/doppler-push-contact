using System.Collections.Generic;

namespace Doppler.PushContact.Models.Models
{
    public class FieldsReplacement
    {
        public bool ReplacementIsMandatory { get; set; }
        public Dictionary<string, string> Fields { get; set; }
    }
}
