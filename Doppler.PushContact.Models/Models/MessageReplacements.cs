using System.Collections.Generic;

namespace Doppler.PushContact.Models.Models
{
    public class MessageReplacements
    {
        public Dictionary<string, string> Values { get; set; }
        public bool ReplacementIsMandatory { get; set; }
    }
}
