using Doppler.PushContact.DTOs;
using Doppler.PushContact.Models.Entities;
using Doppler.PushContact.Models.Enums;
using Doppler.PushContact.Services.Messages;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Doppler.PushContact.Services
{
    public interface IWebPushEventService
    {
        // TODO: revisar este conteo
        Task<WebPushEventSummarizationDTO> GetWebPushEventSummarizationAsync(Guid messageId);
        Task<bool> RegisterWebPushEventAsync(
            string contactId,
            Guid messageId,
            WebPushEventType type,
            string eventDescriptor,
            CancellationToken cancellationToken
        );
        // TODO: revisar este conteo
        Task<int> GetWebPushEventConsumed(string domain, DateTimeOffset dateFrom, DateTimeOffset dateTo);
        Task RegisterWebPushEventsAsync(Guid messageId, IEnumerable<WebPushEvent> webPushEvents, bool registerOnlyFailed);
    }
}
