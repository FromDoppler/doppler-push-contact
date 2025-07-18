using System.Collections.Generic;

namespace Doppler.PushContact.Models.Models
{
    public class FieldsReplacementList
    {
        public bool ReplacementIsMandatory { get; set; }
        public List<VisitorFields> VisitorsFieldsList { get; set; }
    }
}
