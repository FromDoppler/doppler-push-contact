using Doppler.PushContact.DTOs;
using Doppler.PushContact.Models.DTOs;
using Doppler.PushContact.Models.Models;
using Doppler.PushContact.QueuingService.MessageQueueBroker;
using Doppler.PushContact.Repositories.Interfaces;
using Doppler.PushContact.Services.Messages;
using Doppler.PushContact.Services.Queue;
using Doppler.PushContact.Transversal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
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
        private readonly IOptions<WebPushPublisherSettings> _webPushPublisherSettings;

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
            _webPushPublisherSettings = webPushQueueSettings;
        }

        public void ProcessWebPushForVisitors(WebPushDTO messageDTO, FieldsReplacementList visitorsWithReplacements, string authenticationApiToken = null)
        {
            _backgroundQueue.QueueBackgroundQueueItem(async (cancellationToken) =>
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    _logger.LogWarning("WebPush processing was cancelled before starting. MessageId: {MessageId}", messageDTO.MessageId);
                    return;
                }

                if (visitorsWithReplacements.VisitorsFieldsList == null)
                {
                    return;
                }

                var visitorguidsToProcessInBatch = new List<string>();
                foreach (var visitorWithFields in visitorsWithReplacements.VisitorsFieldsList)
                {
                    if (visitorWithFields.ReplaceFields)
                    {
                        await ProcessWebPushForVisitorWithFields(
                            messageDTO,
                            visitorWithFields,
                            visitorsWithReplacements.ReplacementIsMandatory,
                            cancellationToken,
                            authenticationApiToken
                        );
                    }
                    else
                    {
                        visitorguidsToProcessInBatch.Add(visitorWithFields.VisitorGuid);
                    }
                }

                if (visitorguidsToProcessInBatch != null && visitorguidsToProcessInBatch.Count > 0)
                {
                    await ProcessWebPushForVisitorsInBatchesAsync(visitorguidsToProcessInBatch, messageDTO, authenticationApiToken, cancellationToken);
                }
            });
        }

        internal virtual async Task ProcessWebPushForVisitorsInBatchesAsync(
            List<string> visitorGuids,
            WebPushDTO messageDTO,
            string authenticationApiToken = null,
            CancellationToken cancellationToken = default
        )
        {
            try
            {
                _logger.LogInformation(
                    "Starting to process webpush for domain: {Domain}, messageId: {MessageId}",
                    messageDTO.Domain,
                    messageDTO.MessageId
                );

                var deviceTokensBatch = new List<string>();
                int processedDeviceTokensCount = 0;
                int deviceTokenBatchIndex = 0;

                var subscriptionsBatch = new List<SubscriptionInfoDTO>();
                int processedSubscriptionsCount = 0;
                int subscriptionsBatchIndex = 0;

                var sizeFromConfig = _webPushPublisherSettings?.Value?.ProcessPushBatchSize ?? 0;
                var batchSize = sizeFromConfig > 0 ? sizeFromConfig : 500;

                foreach (var visitorGuid in visitorGuids)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        _logger.LogWarning(
                            "WebPush processing was cancelled. Domain: {Domain}, MessageId: {MessageId}",
                            messageDTO.Domain,
                            messageDTO.MessageId
                        );
                        break;
                    }

                    var subscriptionsInfo = await _pushContactRepository.GetAllSubscriptionInfoByVisitorGuidAsync(messageDTO.Domain, visitorGuid);

                    foreach (var subscription in subscriptionsInfo)
                    {
                        if (IsValidSubscription(subscription))
                        {
                            subscriptionsBatch.Add(subscription);

                            if (subscriptionsBatch.Count >= batchSize)
                            {
                                processedSubscriptionsCount += subscriptionsBatch.Count;
                                await ProcessWebPushBatchAsync(subscriptionsBatch, messageDTO, cancellationToken);
                                _logger.LogDebug("Processed subscriptions batch #{BatchIndex}, processed so far: {Count}", ++subscriptionsBatchIndex, processedSubscriptionsCount);
                                subscriptionsBatch.Clear();
                            }
                        }
                        else if (!string.IsNullOrEmpty(subscription.DeviceToken))
                        {
                            deviceTokensBatch.Add(subscription.DeviceToken);

                            if (deviceTokensBatch.Count >= batchSize)
                            {
                                processedDeviceTokensCount += deviceTokensBatch.Count;
                                await _messageSender.SendFirebaseWebPushAsync(messageDTO, deviceTokensBatch, authenticationApiToken);
                                _logger.LogDebug("Processed device tokens batch #{BatchIndex}, processed so far: {Count}", ++deviceTokenBatchIndex, processedDeviceTokensCount);
                                deviceTokensBatch.Clear();
                            }
                        }
                    }
                }

                // send incomplete batches
                if (subscriptionsBatch.Count > 0)
                {
                    processedSubscriptionsCount += subscriptionsBatch.Count;
                    await ProcessWebPushBatchAsync(subscriptionsBatch, messageDTO, cancellationToken);
                    _logger.LogDebug("Processed final subscriptions batch #{BatchIndex}, processed: {Count}", ++subscriptionsBatchIndex, processedSubscriptionsCount);
                }

                if (deviceTokensBatch.Count > 0)
                {
                    processedDeviceTokensCount += deviceTokensBatch.Count;
                    await _messageSender.SendFirebaseWebPushAsync(messageDTO, deviceTokensBatch, authenticationApiToken);
                    _logger.LogDebug("Processed final device tokens batch #{BatchIndex}, processed: {Count}", ++deviceTokenBatchIndex, processedDeviceTokensCount);
                }

                _logger.LogInformation(
                    "Finished processing {TotalSubscriptions} subscriptions and {TotalDeviceToken} deviceTokens, for domain: {Domain}",
                    processedSubscriptionsCount,
                    processedDeviceTokensCount,
                    messageDTO.Domain
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "An unexpected error occurred processing webpush for domain: {Domain} and messageId: {MessageId}.",
                    messageDTO.Domain,
                    messageDTO.MessageId
                );
            }
        }

        public void ProcessWebPushByDomainInBatches(string domain, WebPushDTO messageDTO, string authenticationApiToken = null)
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

                    var deviceTokensBatch = new List<string>();
                    int processedDeviceTokensCount = 0;
                    int deviceTokenBatchBatchIndex = 0;

                    var subscriptionsBatch = new List<SubscriptionInfoDTO>();
                    int processedSubscriptionsCount = 0;
                    int subscriptionsBatchIndex = 0;

                    var sizeFromConfig = _webPushPublisherSettings?.Value?.ProcessPushBatchSize ?? 0;
                    var batchSize = sizeFromConfig > 0 ? sizeFromConfig : 500; // quantity of valid subscriptions/tokens before to fire a batch process

                    // use "await foreach" to consume a method that returns results as a stream
                    await foreach (var subscription in _pushContactRepository.GetSubscriptionInfoByDomainAsStreamAsync(domain, cancellationToken))
                    {
                        if (IsValidSubscription(subscription))
                        {
                            subscriptionsBatch.Add(subscription);

                            if (subscriptionsBatch.Count >= batchSize)
                            {
                                processedSubscriptionsCount += subscriptionsBatch.Count;
                                await ProcessWebPushBatchAsync(subscriptionsBatch, messageDTO, cancellationToken);
                                _logger.LogDebug("Processed subscriptions batch #{BatchIndex}, processed so far: {Count}", ++subscriptionsBatchIndex, processedSubscriptionsCount);
                                subscriptionsBatch.Clear();
                            }
                        }
                        else if (!string.IsNullOrEmpty(subscription.DeviceToken))
                        {
                            deviceTokensBatch.Add(subscription.DeviceToken);

                            if (deviceTokensBatch.Count >= batchSize)
                            {
                                processedDeviceTokensCount += deviceTokensBatch.Count;
                                await _messageSender.SendFirebaseWebPushAsync(messageDTO, deviceTokensBatch, authenticationApiToken);
                                _logger.LogDebug("Processed device tokens batch #{BatchIndex}, processed so far: {Count}", ++deviceTokenBatchBatchIndex, processedDeviceTokensCount);
                                deviceTokensBatch.Clear();
                            }
                        }
                    }

                    if (subscriptionsBatch.Count > 0)
                    {
                        processedSubscriptionsCount += subscriptionsBatch.Count;
                        await ProcessWebPushBatchAsync(subscriptionsBatch, messageDTO, cancellationToken);
                        _logger.LogDebug("Processed final subscriptions batch #{BatchIndex}, processed: {Count}", ++subscriptionsBatchIndex, processedSubscriptionsCount);
                    }

                    if (deviceTokensBatch.Count > 0)
                    {
                        processedDeviceTokensCount += deviceTokensBatch.Count;
                        await _messageSender.SendFirebaseWebPushAsync(messageDTO, deviceTokensBatch, authenticationApiToken);
                        _logger.LogDebug("Processed final device tokens batch #{BatchIndex}, processed: {Count}", ++deviceTokenBatchBatchIndex, processedDeviceTokensCount);
                    }

                    _logger.LogInformation(
                        "Finished processing {TotalSubscriptions} subscriptions and {TotalDeviceToken} deviceTokens, for domain: {Domain}",
                        processedSubscriptionsCount,
                        processedDeviceTokensCount,
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

        internal virtual async Task ProcessWebPushBatchAsync(IEnumerable<SubscriptionInfoDTO> batch, WebPushDTO messageDTO, CancellationToken cancellationToken)
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
                Domain = messageDTO.Domain,
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

        private static string ReplaceFields(string content, Dictionary<string, string> values)
        {
            if (values == null)
            {
                return content;
            }

            var lowerValues = values.ToDictionary(k => k.Key.ToLowerInvariant(), v => v.Value);

            return Regex.Replace(content, @"\[\[\[([\w\.\-]+)\]\]\]", match =>
            {
                var key = match.Groups[1].Value;
                var lowerKey = key.ToLowerInvariant();
                return lowerValues.TryGetValue(lowerKey, out var value) ? value ?? string.Empty : match.Value;
            });
        }

        private List<string> GetMissingReplacements(string content, Dictionary<string, string> values)
        {
            var lowerKeys = (values ?? new Dictionary<string, string>())
                .Keys.Select(k => k.ToLowerInvariant()).ToHashSet();

            var missing = new HashSet<string>();

            var matches = Regex.Matches(content, @"\[\[\[([\w\.\-]+)\]\]\]");
            foreach (Match match in matches)
            {
                var key = match.Groups[1].Value;
                if (!lowerKeys.Contains(key.ToLowerInvariant()))
                {
                    missing.Add(key);
                }
            }

            return missing.ToList();
        }

        internal virtual async Task ProcessWebPushForVisitorWithFields(
            WebPushDTO messageDTO,
            VisitorFields visitorWithFields,
            bool replacementIsMandatory,
            CancellationToken cancellationToken,
            string authenticationApiToken = null
        )
        {
            try
            {
                _logger.LogDebug(
                    "Starting to process webpush for messageId: {MessageId}, visitorGuid: {VisitorGuid}",
                    messageDTO.MessageId,
                    visitorWithFields.VisitorGuid
                );

                var missingFieldsInTitle = GetMissingReplacements(messageDTO.Title, visitorWithFields.Fields);
                var missingFieldsInBody = GetMissingReplacements(messageDTO.Body, visitorWithFields.Fields);
                if (replacementIsMandatory && (missingFieldsInTitle.Count > 0 || missingFieldsInBody.Count > 0))
                {
                    _logger.LogWarning(
                        $"Missing replacements for MessageId: {messageDTO.MessageId} and VisitorGuid: {visitorWithFields.VisitorGuid}. " +
                        $"Missing values in title: [{string.Join(", ", missingFieldsInTitle.Select(x => $"\"{x}\""))}], " +
                        $"missing values in body: [{string.Join(", ", missingFieldsInBody.Select(x => $"\"{x}\""))}]"
                    );
                    return;
                }

                var messageWithReplacedFields = new WebPushDTO()
                {
                    MessageId = messageDTO.MessageId,
                    Title = ReplaceFields(messageDTO.Title, visitorWithFields.Fields),
                    Body = ReplaceFields(messageDTO.Body, visitorWithFields.Fields),
                    ImageUrl = messageDTO.ImageUrl,
                    OnClickLink = messageDTO.OnClickLink,
                    Domain = messageDTO.Domain,
                };

                _logger.LogDebug($"Message with replaced fields: {JsonSerializer.Serialize(messageWithReplacedFields)}");

                var deviceTokens = new List<string>();
                var subscriptionsInfo = await _pushContactRepository.GetAllSubscriptionInfoByVisitorGuidAsync(messageDTO.Domain, visitorWithFields.VisitorGuid);
                foreach (var subscription in subscriptionsInfo)
                {
                    if (IsValidSubscription(subscription))
                    {
                        await EnqueueWebPushAsync(messageWithReplacedFields, subscription.Subscription, subscription.PushContactId, cancellationToken);
                    }
                    else if (!string.IsNullOrEmpty(subscription.DeviceToken))
                    {
                        deviceTokens.Add(subscription.DeviceToken);
                    }
                }

                await _messageSender.SendFirebaseWebPushAsync(messageWithReplacedFields, deviceTokens, authenticationApiToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "An unexpected error occurred processing webpush for messageId: {MessageId} and visitorGuid: {VisitorGuid}.",
                    messageDTO.MessageId,
                    visitorWithFields.VisitorGuid
                );
            }
        }

        private bool IsValidSubscription(SubscriptionInfoDTO subscription)
        {
            return subscription != null &&
                subscription.Subscription != null &&
                subscription.Subscription.Keys != null &&
                !string.IsNullOrEmpty(subscription.Subscription.EndPoint) &&
                !string.IsNullOrEmpty(subscription.Subscription.Keys.Auth) &&
                !string.IsNullOrEmpty(subscription.Subscription.Keys.P256DH);
        }
    }
}
