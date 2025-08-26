using Doppler.PushContact.DTOs;
using Doppler.PushContact.Models.Entities;
using Doppler.PushContact.Models.Enums;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Doppler.PushContact.Repositories.Interfaces
{
    public interface IWebPushEventRepository
    {
        Task<WebPushEventSummarizationDTO> GetWebPushEventSummarization(Guid messageId);
        Task<bool> InsertAsync(WebPushEvent webPushEvent, CancellationToken cancellationToken);
        Task<bool> IsWebPushEventRegistered(string pushContactId, Guid messageId, WebPushEventType type);
        Task<int> GetWebPushEventConsumed(string domain, DateTimeOffset dateFrom, DateTimeOffset dateTo);
    }
}
