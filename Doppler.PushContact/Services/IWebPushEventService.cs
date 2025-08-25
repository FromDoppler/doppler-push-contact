using Doppler.PushContact.DTOs;
using Doppler.PushContact.Models.Enums;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Doppler.PushContact.Services
{
    public interface IWebPushEventService
    {
        Task<WebPushEventSummarizationDTO> GetWebPushEventSummarizationAsync(Guid messageId);
        Task<bool> RegisterWebPushEventAsync(
            string contactId,
            Guid messageId,
            WebPushEventType type,
            CancellationToken cancellationToken
        );
        Task<int> GetWebPushEventConsumed(string domain, DateTimeOffset dateFrom, DateTimeOffset dateTo);
    }
}
