using Doppler.PushContact.Models.Entities;
using System;
using System.Threading.Tasks;

namespace Doppler.PushContact.WebPushSender.Repositories.Interfaces
{
    public interface IMessageRepository
    {
        Task RegisterStatisticsAsync(Guid messageId, WebPushEvent webPushEvent);
    }
}
