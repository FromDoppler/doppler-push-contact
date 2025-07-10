using Doppler.PushContact.DTOs;
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

        public void ProcessWebPushInBatches(string domain, WebPushDTO messageDTO, string authenticationApiToken = null)
        {
            _backgroundQueue.QueueBackgroundQueueItem(async (cancellationToken) =>
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    _logger.LogWarning("WebPush processing was cancelled before starting. Domain: {Domain}, MessageId: {MessageId}", domain, messageDTO.MessageId);
                    return;
                }

                try
                {
                    _logger.LogInformation("Starting to process webpush for domain: {Domain}, messageId: {MessageId}", domain, messageDTO.MessageId);

                    var deviceTokens = new List<string>();
                    var batchToEnqueueSubscriptions = new List<SubscriptionInfoDTO>();

                    int processedCount = 0;
                    int batchIndex = 0;
                    const int batchSize = 5; // quantity of valid subscriptions before to fire a batch process

                    // use "await foreach" to consume a method that returns results as a stream
                    await foreach (var subscription in _pushContactRepository.GetSubscriptionInfoByDomainAsStreamAsync(domain, cancellationToken))
                    {
                        if (subscription.Subscription != null &&
                            subscription.Subscription.Keys != null &&
                            !string.IsNullOrEmpty(subscription.Subscription.EndPoint) &&
                            !string.IsNullOrEmpty(subscription.Subscription.Keys.Auth) &&
                            !string.IsNullOrEmpty(subscription.Subscription.Keys.P256DH))
                        {
                            batchToEnqueueSubscriptions.Add(subscription);
                            if (batchToEnqueueSubscriptions.Count >= batchSize)
                            {
                                processedCount += batchToEnqueueSubscriptions.Count;
                                _logger.LogDebug("Processing batch #{BatchIndex}, total processed so far: {Count}", ++batchIndex, processedCount);
                                await ProcessWebPushBatchAsync(batchToEnqueueSubscriptions, messageDTO, cancellationToken);
                                batchToEnqueueSubscriptions.Clear();
                            }
                        }
                        else if (!string.IsNullOrEmpty(subscription.DeviceToken))
                        {
                            deviceTokens.Add(subscription.DeviceToken);
                        }
                    }

                    if (batchToEnqueueSubscriptions.Count > 0)
                    {
                        processedCount += batchToEnqueueSubscriptions.Count;
                        _logger.LogDebug("Processing final batch #{BatchIndex}, total processed so far: {Count}", ++batchIndex, processedCount);
                        await ProcessWebPushBatchAsync(batchToEnqueueSubscriptions, messageDTO, cancellationToken);
                    }

                    if (deviceTokens.Count > 0)
                    {
                        await _messageSender.SendFirebaseWebPushAsync(messageDTO, deviceTokens, authenticationApiToken);
                    }

                    _logger.LogInformation(
                        "Finished processing {TotalSubscriptions} subscriptions and {TotalDeviceToken} deviceTokens, for domain: {Domain}",
                        processedCount,
                        deviceTokens.Count,
                        domain
                    );
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

        private async Task ProcessWebPushBatchAsync(IEnumerable<SubscriptionInfoDTO> batch, WebPushDTO messageDTO, CancellationToken cancellationToken)
        {
            await Parallel.ForEachAsync(batch, cancellationToken, async (subscription, ct) =>
            {
                try
                {
                    await EnqueueWebPushAsync(messageDTO, subscription.Subscription, subscription.PushContactId, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Error enqueuing push for subscription with ID: {PushContactId}",
                        subscription.PushContactId
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
