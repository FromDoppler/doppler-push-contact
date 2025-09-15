using Doppler.PushContact.DTOs;
using Doppler.PushContact.Models.Enums;
using Doppler.PushContact.Services.Messages;
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
        Task RegisterWebPushEventsAsync(string domain, Guid messageId, SendMessageResult sendMessageResult);
    }
}
