using Doppler.PushContact.Models.DTOs;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Doppler.PushContact.Services.Messages
{
    public interface IMessageSender
    {
        Task<SendMessageResult> SendAsync(string title, string body, IEnumerable<string> targetDeviceTokens, string onClickLink = null, string imageUrl = null, string pushApiToken = null);
        void ValidateMessage(string title, string body, string onClickLink, string imageUrl);
        Task SendFirebaseWebPushAsync(WebPushDTO webPushDTO, List<string> deviceTokens, string authenticationApiToken);
    }
}
