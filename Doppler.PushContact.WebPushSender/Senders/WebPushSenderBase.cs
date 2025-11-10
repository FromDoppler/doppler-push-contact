using Doppler.PushContact.Models.DTOs;
using Doppler.PushContact.QueuingService.MessageQueueBroker;
using Doppler.PushContact.Transversal;
using Doppler.PushContact.WebPushSender.DTOs.WebPushApi;
using Doppler.PushContact.WebPushSender.Repositories.Interfaces;
using Flurl;
using Flurl.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Doppler.PushContact.WebPushSender.Senders
{
    public abstract class WebPushSenderBase : IWebPushSender
    {
        private const string PUSHAPI_SENDWEBPUSH_PATH = "webpush";

        private readonly IMessageQueueSubscriber _messageQueueSubscriber;
        protected readonly ILogger _logger;
        protected readonly string _queueName;
        private IDisposable _queueSubscription;
        private readonly string _pushApiUrl;
        protected readonly IWebPushEventRepository _weshPushEventRepository;
        private readonly string _actionClickEventEndpointPath;
        private readonly string _pushContactApiUrl;

        protected WebPushSenderBase(
            IOptions<WebPushSenderSettings> webPushSenderSettings,
            IMessageQueueSubscriber messageQueueSubscriber,
            ILogger logger,
            IWebPushEventRepository weshPushEventRepository
        )
        {
            _messageQueueSubscriber = messageQueueSubscriber;
            _logger = logger;
            _queueName = webPushSenderSettings.Value.QueueName;
            _pushApiUrl = webPushSenderSettings.Value.PushApiUrl;
            _weshPushEventRepository = weshPushEventRepository;
            _actionClickEventEndpointPath = webPushSenderSettings.Value.ActionClickEventEndpointPath;
            _pushContactApiUrl = webPushSenderSettings.Value.PushContactApiUrl;
        }

        public async Task StartListeningAsync(CancellationToken cancellationToken)
        {
            _queueSubscription = await _messageQueueSubscriber.SubscribeAsync<DopplerWebPushDTO>(
                HandleMessageAsync,
                _queueName,
                cancellationToken
            );
        }

        public void StopListeningAsync()
        {
            if (_queueSubscription != null)
            {
                _queueSubscription.Dispose();
            }
        }

        public abstract Task HandleMessageAsync(DopplerWebPushDTO message);

        protected virtual async Task<WebPushProcessingResultDTO> SendWebPush(DopplerWebPushDTO message)
        {
            SendMessageResponse sendMessageResponse = null;
            try
            {
                sendMessageResponse = await _pushApiUrl
                .AppendPathSegment(PUSHAPI_SENDWEBPUSH_PATH)
                // TODO: analyze options to handle (or remove) push api token
                //.WithOAuthBearerToken(pushApiToken)
                .PostJsonAsync(new
                {
                    subscriptions = new[]
                    {
                        new
                        {
                            endpoint = message.Subscription.EndPoint,
                            p256DH = message.Subscription.Keys.P256DH,
                            auth = message.Subscription.Keys.Auth,
                            subscriptionExtraData = new
                            {
                                clickedEventEndpoint = message.ClickedEventEndpoint,
                                receivedEventEndpoint = message.ReceivedEventEndpoint,
                                actionEventEndpoints = GetActionEventEndpoints(message, message.Actions),
                            },
                        },
                    },
                    notificationTitle = message.Title,
                    notificationBody = message.Body,
                    notificationOnClickLink = message.OnClickLink,
                    imageUrl = message.ImageUrl,
                    iconUrl = message.IconUrl,
                    actions = message.Actions?.Select(a => new
                    {
                        action = a.Action,
                        title = a.Title,
                        icon = a.Icon,
                        link = a.Link,
                    }).ToList()
                })
                .ReceiveJson<SendMessageResponse>();
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "An unexpected error occurred sending a web push to endpoint: {endpoint} for pushContactId: {pushContactId}.",
                    message.Subscription.EndPoint,
                    message.PushContactId
                );

                return new WebPushProcessingResultDTO()
                {
                    FailedProcessing = true,
                };
            }

            return ProcessWebPushResponse(sendMessageResponse);
        }

        private WebPushProcessingResultDTO ProcessWebPushResponse(SendMessageResponse sendMessageResponse)
        {
            if (sendMessageResponse == null)
            {
                return new WebPushProcessingResultDTO { FailedProcessing = false };
            }

            WebPushProcessingResultDTO processingResult = new WebPushProcessingResultDTO();

            // it has just one response
            var response = sendMessageResponse.Responses?.FirstOrDefault();
            processingResult.SuccessfullyDelivered = response != null && response.IsSuccess == true;

            if (response != null && response.IsSuccess == false && response.Exception != null)
            {
                switch (response.Exception.MessagingErrorCode)
                {
                    case 429:
                        // TODO: log information to be analyzed and take proper actions
                        _logger.LogWarning
                        (
                            "(Error {WebPushResponseStatusCode}) Too many requests:\n\tSubscription: {Subscription}\n\tException: {WebPushResponseException}",
                            response.Exception.MessagingErrorCode,
                            JsonConvert.SerializeObject(response.Subscription),
                            JsonConvert.SerializeObject(response.Exception)
                        );

                        processingResult.LimitsExceeded = true;
                        break;

                    case (int)HttpStatusCode.NotFound:
                    case (int)HttpStatusCode.Gone:
                    case (int)HttpStatusCode.Unauthorized:
                        _logger.LogDebug
                        (
                            "(Error {WebPushResponseStatusCode}):\n\tSubscription: {Subscription}\n\tException: {WebPushResponseException}",
                            response.Exception.MessagingErrorCode,
                            JsonConvert.SerializeObject(response.Subscription),
                            JsonConvert.SerializeObject(response.Exception)
                        );

                        processingResult.InvalidSubscription = true;
                        break;
                    default:
                        _logger.LogError
                        (
                            "(Error {WebPushResponseStatusCode}):\n\tSubscription: {Subscription}\n\tException: {WebPushResponseException}",
                            response.Exception.MessagingErrorCode,
                            JsonConvert.SerializeObject(response.Subscription),
                            JsonConvert.SerializeObject(response.Exception)
                        );

                        processingResult.UnknownFail = true;
                        processingResult.ErrorMessage = response.Exception.Message;
                        break;
                }
            }

            return processingResult;
        }

        private string GetEndpointToRegisterEvent(string endpointPath, string pushContactId, string messageId, string actionName)
        {
            if (string.IsNullOrEmpty(endpointPath) ||
                string.IsNullOrEmpty(pushContactId) ||
                string.IsNullOrEmpty(messageId) ||
                string.IsNullOrEmpty(_pushContactApiUrl)
            )
            {
                return null;
            }

            var encryptedContactId = EncryptionHelper.Encrypt(pushContactId, useBase64Url: true);
            var encryptedMessageId = EncryptionHelper.Encrypt(messageId, useBase64Url: true);

            var endpointResult = endpointPath
                .Replace("[pushContactApiUrl]", _pushContactApiUrl)
                .Replace("[encryptedContactId]", encryptedContactId)
                .Replace("[encryptedMessageId]", encryptedMessageId);

            if (!string.IsNullOrEmpty(actionName))
            {
                endpointResult = endpointResult.Replace("[actionName]", actionName);
            }

            return endpointResult;
        }

        private Dictionary<string, string> GetActionEventEndpoints(DopplerWebPushDTO webPushDTO, List<MessageActionDTO> messageActions)
        {
            var actionEventEndpoints = new Dictionary<string, string>();

            if (messageActions != null)
            {
                foreach (var action in messageActions)
                {
                    var actionEventEndpoint = GetEndpointToRegisterEvent(
                        _actionClickEventEndpointPath,
                        webPushDTO.PushContactId,
                        webPushDTO.MessageId.ToString(),
                        action.Action
                    );
                    actionEventEndpoints.Add(action.Action, actionEventEndpoint);
                }
            }

            return actionEventEndpoints;
        }
    }
}
