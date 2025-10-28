using Doppler.PushContact.Models.Entities;
using System;
using System.Threading.Tasks;

namespace Doppler.PushContact.WebPushSender.Repositories.Interfaces
{
    public interface IMessageRepository
    {
        Task RegisterShippingStatisticsAsync(Guid messageId, WebPushEvent webPushEvent);
    }
}
