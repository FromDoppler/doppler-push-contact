using Doppler.PushContact.Models.DTOs;
using System;
using System.Collections.Generic;

namespace Doppler.PushContact.Models.Models
{
    public class MessageStatsGroupedByPeriodModel
    {
        public string Domain { get; set; }
        public List<Guid> MessageIds { get; set; }
        public DateTimeOffset DateFrom { get; set; }
        public DateTimeOffset DateTo { get; set; }
        public string GroupedPeriod { get; set; }
        public MessageStatsTotals Totals { get; set; }
        public List<MessageStatsPeriodDTO> Periods { get; set; }
    }
}
