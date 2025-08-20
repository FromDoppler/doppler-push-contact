using Doppler.PushContact.Models.DTOs;
using Doppler.PushContact.Services.Messages.ExternalContracts;
using Flurl;
using Flurl.Http;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Doppler.PushContact.Services.Messages
{
    public class MessageSender : IMessageSender
    {
        private readonly MessageSenderSettings _messageSenderSettings;
        private readonly IPushApiTokenGetter _pushApiTokenGetter;
        private readonly IMessageRepository _messageRepository;
        private readonly IPushContactService _pushContactService;

        public MessageSender(
            IOptions<MessageSenderSettings> messageSenderSettings,
            IPushApiTokenGetter pushApiTokenGetter,
            IMessageRepository messageRepository,
            IPushContactService pushContactService
        )
        {
            _messageSenderSettings = messageSenderSettings.Value;
            _pushApiTokenGetter = pushApiTokenGetter;
            _messageRepository = messageRepository;
            _pushContactService = pushContactService;
        }

        public async Task<SendMessageResult> SendAsync(string title, string body, IEnumerable<string> targetDeviceTokens, string onClickLink = null, string imageUrl = null, string pushApiToken = null)
        {
            ValidateMessage(title, body, onClickLink, imageUrl);

            if (targetDeviceTokens == null || !targetDeviceTokens.Any())
            {
                throw new ArgumentException($"'{nameof(targetDeviceTokens)}' cannot be null or empty.", nameof(targetDeviceTokens));
            }

            // TODO: use adhock token here.
            // It is recovering our client API request to be resusen to request to Push API,
            // but maybe it will not be acceptable in all scenarios.
            if (string.IsNullOrEmpty(pushApiToken))
            {
                pushApiToken = await _pushApiTokenGetter.GetTokenAsync();
            }

            SendMessageResponse responseBody = new();
            responseBody.Responses = new();

            var tokensSkipped = 0;

            do
            {
                IEnumerable<string> tokensSelected = targetDeviceTokens.Skip(tokensSkipped).Take(_messageSenderSettings.PushTokensLimit);
                tokensSkipped += tokensSelected.Count();

                SendMessageResponse messageResponse = await _messageSenderSettings.PushApiUrl
                .AppendPathSegment("message")
                .WithOAuthBearerToken(pushApiToken)
                .PostJsonAsync(new
                {
                    notificationTitle = title,
                    notificationBody = body,
                    NotificationOnClickLink = onClickLink,
                    tokens = tokensSelected,
                    ImageUrl = imageUrl
                })
                .ReceiveJson<SendMessageResponse>();

                responseBody.Responses.AddRange(messageResponse.Responses);

            } while (tokensSkipped < targetDeviceTokens.Count());

            return new SendMessageResult
            {
                SendMessageTargetResult = responseBody.Responses.Select(x => new SendMessageTargetResult
                {
                    TargetDeviceToken = x.DeviceToken,
                    IsSuccess = x.IsSuccess,
                    IsValidTargetDeviceToken = x.IsSuccess || _messageSenderSettings.FatalMessagingErrorCodes.All(y => y != x.Exception.MessagingErrorCode),
                    NotSuccessErrorDetails = !x.IsSuccess ? $"{nameof(x.Exception.MessagingErrorCode)} {x.Exception.MessagingErrorCode} - {nameof(x.Exception.Message)} {x.Exception.Message}" : null
                })
            };
        }

        public async Task<Guid> AddMessageAsync(string domain, string title, string body, string onClickLink, string imageUrl)
        {
            ValidateMessage(title, body, onClickLink, imageUrl);

            var messageId = Guid.NewGuid();
            await _messageRepository.AddAsync(messageId, domain, title, body, onClickLink, 0, 0, 0, imageUrl);

            return messageId;
        }

        public void ValidateMessage(string title, string body, string onClickLink, string imageUrl)
        {
            if (string.IsNullOrEmpty(title))
            {
                throw new ArgumentException($"'{nameof(title)}' cannot be null or empty.", nameof(title));
            }

            if (string.IsNullOrEmpty(body))
            {
                throw new ArgumentException($"'{nameof(body)}' cannot be null or empty.", nameof(body));
            }

            if (!string.IsNullOrEmpty(onClickLink)
                && (!Uri.TryCreate(onClickLink, UriKind.Absolute, out var onClickLinkResult) || onClickLinkResult.Scheme != Uri.UriSchemeHttps))
            {
                throw new ArgumentException($"'{nameof(onClickLink)}' must be an absolute URL with HTTPS scheme.", nameof(onClickLink));
            }

            if (!string.IsNullOrEmpty(imageUrl)
                && (!Uri.TryCreate(imageUrl, UriKind.Absolute, out var imgUrlResult) || imgUrlResult.Scheme != Uri.UriSchemeHttps))
            {
                throw new ArgumentException($"'{nameof(imageUrl)}' must be an absolute URL with HTTPS scheme.", nameof(imageUrl));
            }
        }

        public async Task SendFirebaseWebPushAsync(WebPushDTO webPushDTO, List<string> deviceTokens, string authenticationApiToken)
        {
            if (deviceTokens == null || !deviceTokens.Any())
            {
                return;
            }

            var sendMessageResult = await SendAsync(
                webPushDTO.Title,
                webPushDTO.Body,
                deviceTokens,
                webPushDTO.OnClickLink,
                webPushDTO.ImageUrl,
                authenticationApiToken
            );

            await _pushContactService.AddHistoryEventsAndMarkDeletedContactsAsync(webPushDTO.MessageId, sendMessageResult);

            await RegisterStatisticsAsync(webPushDTO.MessageId, sendMessageResult);
        }

        private async Task RegisterStatisticsAsync(Guid messageId, SendMessageResult sendMessageResult)
        {
            var sent = sendMessageResult.SendMessageTargetResult.Count();
            var delivered = sendMessageResult.SendMessageTargetResult.Count(x => x.IsSuccess);
            var notDelivered = sent - delivered;

            await _messageRepository.UpdateDeliveriesAsync(messageId, sent, delivered, notDelivered);
        }
    }
}
