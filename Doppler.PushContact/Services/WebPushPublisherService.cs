using Doppler.PushContact.Models.DTOs;
using Doppler.PushContact.QueuingService.MessageQueueBroker;
using Doppler.PushContact.Repositories.Interfaces;
using Doppler.PushContact.Services.Messages;
using Doppler.PushContact.Services.Queue;
using Doppler.PushContact.Transversal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Doppler.PushContact.Services
{
    public class WebPushPublisherService : IWebPushPublisherService
    {
        private readonly IPushContactRepository _pushContactRepository;
        private readonly IBackgroundQueue _backgroundQueue;
        private readonly IMessageSender _messageSender;
        private readonly ILogger<WebPushPublisherService> _logger;
        private readonly IMessageQueuePublisher _messageQueuePublisher;
        private readonly Dictionary<string, List<string>> _pushEndpointMappings;

        private const string QUEUE_NAME_SUFIX = "webpush.queue";
        private const string DEFAULT_QUEUE_NAME = $"default.{QUEUE_NAME_SUFIX}";

        private readonly string _clickedEventEndpointPath;
        private readonly string _receivedEventEndpointPath;
        private readonly string _pushContactApiUrl;

        public WebPushPublisherService(
            IPushContactRepository pushContactRepository,
            IBackgroundQueue backgroundQueue,
            IMessageSender messageSender,
            ILogger<WebPushPublisherService> logger,
            IMessageQueuePublisher messageQueuePublisher,
            IOptions<WebPushPublisherSettings> webPushQueueSettings
        )
        {
            _pushContactRepository = pushContactRepository;
            _backgroundQueue = backgroundQueue;
            _messageSender = messageSender;
            _logger = logger;
            _messageQueuePublisher = messageQueuePublisher;
            _pushEndpointMappings = webPushQueueSettings.Value.PushEndpointMappings;
            _pushContactApiUrl = webPushQueueSettings.Value.PushContactApiUrl;
            _clickedEventEndpointPath = webPushQueueSettings.Value.ClickedEventEndpointPath;
            _receivedEventEndpointPath = webPushQueueSettings.Value.ReceivedEventEndpointPath;
        }

        public void ProcessWebPush(string domain, WebPushDTO messageDTO, string authenticationApiToken = null)
        {
            _backgroundQueue.QueueBackgroundQueueItem(async (cancellationToken) =>
            {
                try
                {
                    var deviceTokens = new List<string>();
                    var subscriptionsInfo = await _pushContactRepository.GetAllSubscriptionInfoByDomainAsync(domain);
                    foreach (var subscription in subscriptionsInfo)
                    {
                        if (subscription.Subscription != null &&
                            subscription.Subscription.Keys != null &&
                            !string.IsNullOrEmpty(subscription.Subscription.EndPoint) &&
                            !string.IsNullOrEmpty(subscription.Subscription.Keys.Auth) &&
                            !string.IsNullOrEmpty(subscription.Subscription.Keys.P256DH)
                        )
                        {
                            await EnqueueWebPushAsync(messageDTO, subscription.Subscription, subscription.PushContactId, cancellationToken);
                        }
                        else if (!string.IsNullOrEmpty(subscription.DeviceToken))
                        {
                            deviceTokens.Add(subscription.DeviceToken);
                        }
                    }

                    await _messageSender.SendFirebaseWebPushAsync(messageDTO, deviceTokens, authenticationApiToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "An unexpected error occurred processing webpush for domain: {domain} and messageId: {messageId}.",
                        domain,
                        messageDTO.MessageId
                    );
                }
            });
        }

        private async Task EnqueueWebPushAsync(
            WebPushDTO messageDTO,
            SubscriptionDTO subscription,
            string pushContactId,
            CancellationToken cancellationToken
        )
        {
            var clickedEventEndpoint = SanityzeEndpointToRegisterEvent(_clickedEventEndpointPath, pushContactId, messageDTO.MessageId.ToString());
            var receivedEventEndpoint = SanityzeEndpointToRegisterEvent(_receivedEventEndpointPath, pushContactId, messageDTO.MessageId.ToString());

            var webPushMessage = new DopplerWebPushDTO()
            {
                Title = messageDTO.Title,
                Body = messageDTO.Body,
                OnClickLink = messageDTO.OnClickLink,
                ImageUrl = messageDTO.ImageUrl,
                Subscription = subscription,
                MessageId = messageDTO.MessageId,
                PushContactId = pushContactId,
                ClickedEventEndpoint = clickedEventEndpoint,
                ReceivedEventEndpoint = receivedEventEndpoint,
            };

            string queueName = GetQueueName(subscription.EndPoint);

            try
            {
                await _messageQueuePublisher.PublishAsync(webPushMessage, queueName, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "An unexpected error occurred enqueuing webpush for messageId: {messageId} and subscription: {subscription}.",
                    messageDTO.MessageId,
                    JsonSerializer.Serialize(subscription, new JsonSerializerOptions { WriteIndented = true })
                );
            }
        }
        public string GetQueueName(string endpoint)
        {
            foreach (var mapping in _pushEndpointMappings)
            {
                foreach (var url in mapping.Value)
                {
                    if (endpoint.StartsWith(url, StringComparison.OrdinalIgnoreCase))
                    {
                        return $"{mapping.Key}.{QUEUE_NAME_SUFIX}";
                    }
                }
            }

            return DEFAULT_QUEUE_NAME;
        }

        public string SanityzeEndpointToRegisterEvent(string endpointPath, string pushContactId, string messageId)
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

            return endpointPath
                .Replace("[pushContactApiUrl]", _pushContactApiUrl)
                .Replace("[encryptedContactId]", encryptedContactId)
                .Replace("[encryptedMessageId]", encryptedMessageId);
        }
    }
}
