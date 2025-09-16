using Doppler.PushContact.ApiModels;
using Doppler.PushContact.Models.Entities;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Doppler.PushContact.Services.Messages
{
    public interface IMessageRepository
    {
        Task AddAsync(Guid messageId, string domain, string title, string body, string onClickLink, int sent, int delivered, int notDelivered, string imageUrl);

        Task<MessageDetails> GetMessageDetailsAsync(string domain, Guid messageId);

        Task<MessageDetails> GetMessageDetailsByMessageIdAsync(Guid messageId);

        Task<ApiPage<MessageDeliveryResult>> GetMessages(int page, int per_page, DateTimeOffset from, DateTimeOffset to);

        Task UpdateDeliveriesAsync(Guid messageId, int sent, int delivered, int notDelivered);

        Task IncrementMessageStats(Guid messageId, int sent, int delivered, int notDelivered);
        Task<string> GetMessageDomainAsync(Guid messageId);
        Task<int> GetMessageSends(string domain, DateTimeOffset dateFrom, DateTimeOffset dateTo);
        Task RegisterStatisticsAsync(Guid messageId, IEnumerable<WebPushEvent> webPushEvents);
    }
}
