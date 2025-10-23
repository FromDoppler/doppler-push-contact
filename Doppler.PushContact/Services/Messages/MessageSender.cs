using Doppler.PushContact.Models.DTOs;
using Doppler.PushContact.Models.Entities;
using Doppler.PushContact.Models.Enums;
using Doppler.PushContact.Services.Messages.ExternalContracts;
using Flurl;
using Flurl.Http;
using Microsoft.Extensions.Logging;
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
        private readonly IWebPushEventService _webPushEventService;
        private readonly IMessageStatsService _messageStatsService;
        private readonly ILogger<MessageSender> _logger;

        public MessageSender(
            IOptions<MessageSenderSettings> messageSenderSettings,
            IPushApiTokenGetter pushApiTokenGetter,
            IMessageRepository messageRepository,
            IPushContactService pushContactService,
            IWebPushEventService webPushEventService,
            IMessageStatsService messageStatsService,
            ILogger<MessageSender> logger
        )
        {
            _messageSenderSettings = messageSenderSettings.Value;
            _pushApiTokenGetter = pushApiTokenGetter;
            _messageRepository = messageRepository;
            _pushContactService = pushContactService;
            _webPushEventService = webPushEventService;
            _messageStatsService = messageStatsService;
            _logger = logger;
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
            //if (string.IsNullOrEmpty(pushApiToken))
            //{
            //    pushApiToken = await _pushApiTokenGetter.GetTokenAsync();
            //}

            // TODO: analyze better how to handle authentication and token to consume the push api
            pushApiToken = _messageSenderSettings.AuthenticationToken;
            _logger.LogInformation("APITOKEN en MessageSender.SendAsync1: {ApiToken}", pushApiToken);

            SendMessageResponse responseBody = new();
            responseBody.Responses = new();

            var tokensSkipped = 0;

            try
            {
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
            }
            catch (FlurlHttpException ex)
            {
                string responseContent = null;
                IEnumerable<string> headers = null;

                try
                {
                    responseContent = await ex.GetResponseStringAsync();
                }
                catch { /* ignorar si no se puede leer el body */ }

                try
                {
                    headers = ex.Call?.Response?.Headers?.Select(h => $"{h.Name}: {h.Value}");
                }
                catch { /* ignorar si no se pueden leer headers */ }

                _logger.LogError(ex,
                    "Error al llamar a Push API. StatusCode={StatusCode}, Url={Url}, Response={Response}, Headers={Headers}",
                    ex.Call?.Response?.StatusCode.ToString() ?? "N/A",
                    ex.Call?.Request?.Url?.ToString() ?? "N/A",
                    responseContent ?? "No response body",
                    headers != null ? string.Join(" | ", headers) : "No headers");

                throw;
            }

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

        // TODO: this method should be removed. Message creation should not be responsibility of MessageSender
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

            try
            {
                var sendMessageResult = await SendAsync(
                    webPushDTO.Title,
                    webPushDTO.Body,
                    deviceTokens,
                    webPushDTO.OnClickLink,
                    webPushDTO.ImageUrl,
                    authenticationApiToken
                );

                await _pushContactService.MarkDeletedContactsAsync(webPushDTO.MessageId, sendMessageResult);

                var webPushEvents = MapWebPushEventsFromFirebaseResult(webPushDTO.Domain, webPushDTO.MessageId, sendMessageResult);
                await _webPushEventService.RegisterWebPushEventsAsync(webPushDTO.MessageId, webPushEvents, false);

                // TODO: the stats in Message should be removed?
                await _messageRepository.RegisterStatisticsAsync(webPushDTO.MessageId, webPushEvents);
                await _messageStatsService.RegisterMessageStatsAsync(webPushEvents);
            }
            catch (ArgumentException argEx)
            {
                _logger.LogError(
                    "An error occurred sending webpush using Firebase for messageId: {messageId} and ApiToken: {ApiToken}. Error: {ErrorMessage}.",
                    webPushDTO.MessageId,
                    authenticationApiToken,
                    argEx.Message
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "An unexpected error occurred sending webpush using Firebase for messageId: {messageId} and ApiToken: {ApiToken}.",
                    authenticationApiToken,
                    webPushDTO.MessageId
                );
            }
        }

        private IEnumerable<WebPushEvent> MapWebPushEventsFromFirebaseResult(string domain, Guid messageId, SendMessageResult sendMessageResult)
        {
            if (sendMessageResult?.SendMessageTargetResult == null)
            {
                return Enumerable.Empty<WebPushEvent>();
            }

            return sendMessageResult.SendMessageTargetResult.Select(sendResult => new WebPushEvent
            {
                Domain = domain,
                MessageId = messageId,
                DeviceToken = sendResult.TargetDeviceToken,
                Date = DateTime.UtcNow,
                Type = sendResult.IsSuccess ? (int)WebPushEventType.Delivered : (int)WebPushEventType.DeliveryFailed,
                SubType = sendResult.IsSuccess
                    ? (int)WebPushEventSubType.None
                    : sendResult.IsValidTargetDeviceToken
                        ? (int)WebPushEventSubType.UnknownFailure
                        : (int)WebPushEventSubType.InvalidSubcription,
                ErrorMessage = sendResult.NotSuccessErrorDetails,
            });
        }
    }
}
