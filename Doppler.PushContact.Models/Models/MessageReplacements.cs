using System.Collections.Generic;

namespace Doppler.PushContact.Models.Models
{
    public class MessageReplacements
    {
        public Dictionary<string, string> FieldsToReplace { get; set; }
        public bool ReplacementIsMandatory { get; set; }
    }
}
