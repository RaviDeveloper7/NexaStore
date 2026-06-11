// AzureServiceBusPublisher.cs — implements IMessageBusPublisher using Azure Service Bus SDK.
// IN: This class is called EXCLUSIVELY by OutboxProcessorFunction.
// Handlers NEVER call this directly — that's the entire Outbox Pattern.
// If a handler published directly to Service Bus and the publish failed,
// the order would be saved but the event lost. The Outbox intermediary
// guarantees at-least-once delivery regardless of Service Bus availability.
//
// IN: ServiceBusClient vs ServiceBusSender.
// ServiceBusClient: the top-level client — manages connections and senders.
//   Register as Singleton — one connection pool for the app lifetime.
// ServiceBusSender: topic-specific sender — lightweight, cheap to create.
//   We create one sender per topic name on demand and cache them.
//   Senders are thread-safe and designed for reuse.

using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NexaStore.Application.Common.Interfaces.Services;

namespace NexaStore.Infrastructure.MessageBus;

public class AzureServiceBusPublisher : IMessageBusPublisher, IAsyncDisposable
{
    private readonly ServiceBusClient _client;
    private readonly ServiceBusSettings _settings;
    private readonly ILogger<AzureServiceBusPublisher> _logger;

    // Cache of topic senders — one per topic name.
    // IN: Creating a new sender per publish call is wasteful — each sender
    // holds a reference to the underlying AMQP link.
    // Dictionary cache means one sender per topic, reused across all publishes.
    // Thread-safe read after initialisation — senders are added lazily on first use.
    private readonly Dictionary<string, ServiceBusSender> _senders = new();

    // Lock for lazy sender initialisation — prevents duplicate sender creation
    // under concurrent requests.
    private readonly SemaphoreSlim _senderLock = new(1, 1);

    public AzureServiceBusPublisher(
        ServiceBusClient client,IOptions<ServiceBusSettings> settings,
        ILogger<AzureServiceBusPublisher> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task PublishAsync(string topicName,string message,CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_settings.ConnectionString))
        {
            // IN: Guard for local dev where Service Bus isn't configured.
            // Log and return — don't throw. OutboxProcessorFunction should not
            // crash in local dev just because Service Bus credentials are absent.
            _logger.LogWarning(
                "ServiceBus:ConnectionString is not configured. " +
                "Message to topic '{Topic}' was NOT published.",
                topicName);
            return;
        }

        // Get or create the sender for this topic
        var sender = await GetOrCreateSenderAsync(topicName);

        try
        {
            // Build the ServiceBusMessage from the JSON string
            // IN: ServiceBusMessage carries the body as BinaryData.
            // BinaryData.FromString() encodes the JSON string as UTF-8 bytes.
            // ContentType tells consumers the message body is JSON —
            // useful for consumers that need to handle multiple content types.
            var serviceBusMessage = new ServiceBusMessage(
                BinaryData.FromString(message))
            {
                ContentType = "application/json",

                // IN: MessageId for duplicate detection at the Service Bus level.
                // If Service Bus duplicate detection is enabled (configured in Azure Portal),
                // it will reject messages with duplicate MessageIds within the
                // duplicate detection window (default 10 minutes).
                // Belt-and-suspenders: consumer also checks for duplicates via MessageId.
                MessageId = Guid.NewGuid().ToString(),

                // Subject helps consumers quickly identify the message type
                // without deserialising the body — useful for filtering rules
                // on Service Bus subscriptions.
                Subject = topicName
            };

            await sender.SendMessageAsync(serviceBusMessage, cancellationToken);

            _logger.LogInformation(
                "Message published to Service Bus topic '{Topic}'. " +
                "MessageId: {MessageId}",
                topicName,
                serviceBusMessage.MessageId);
        }
        catch (ServiceBusException ex) when (ex.IsTransient)
        {
            // IN: Transient Service Bus exceptions (network blip, throttling).
            // Re-throw — the OutboxProcessorFunction's retry logic handles this.
            // The OutboxMessage.ProcessedAt remains null — next timer execution retries.
            // This is exactly the Outbox Pattern's resilience guarantee in action.
            _logger.LogWarning(ex,
                "Transient Service Bus error publishing to '{Topic}'. " +
                "OutboxMessage will be retried on next processor execution.",
                topicName);
            throw;
        }
        catch (ServiceBusException ex)
        {
            // Non-transient Service Bus exception — configuration error, auth failure.
            // Log as Error — this needs human intervention.
            _logger.LogError(ex,
                "Non-transient Service Bus error publishing to '{Topic}'. " +
                "Check ServiceBus configuration and topic existence.",
                topicName);
            throw;
        }
    }

    // Lazy sender creation with double-check locking pattern
    private async Task<ServiceBusSender> GetOrCreateSenderAsync(string topicName)
    {
        // Fast path — sender already exists (common case after first publish)
        if (_senders.TryGetValue(topicName, out var existingSender))
            return existingSender;

        // Slow path — acquire lock and create sender
        await _senderLock.WaitAsync();
        try
        {
            // IN: Double-check after acquiring lock — another thread may have
            // created the sender between our TryGetValue and WaitAsync calls.
            // Without this check, two concurrent first-publishes to the same topic
            // would both create senders — the second overwrites the first, leaking
            // the AMQP link from the first sender.
            if (_senders.TryGetValue(topicName, out var doubleCheckSender))
                return doubleCheckSender;

            var newSender = _client.CreateSender(topicName);
            _senders[topicName] = newSender;

            _logger.LogDebug(
                "Created ServiceBusSender for topic '{Topic}'", topicName);

            return newSender;
        }
        finally
        {
            _senderLock.Release();
        }
    }

    // IN: IAsyncDisposable — properly dispose all senders and the client.
    // ServiceBusSender holds an AMQP link (a persistent connection channel).
    // Not disposing it leaves the link open on the broker side until timeout.
    // ServiceBusClient holds the underlying AMQP connection.
    // Both must be disposed when the application shuts down.
    // IAsyncDisposable is the correct pattern for async cleanup — DisposeAsync
    // can await the async close operations, unlike synchronous Dispose().
    public async ValueTask DisposeAsync()
    {
        // Dispose all cached senders
        foreach (var sender in _senders.Values)
        {
            await sender.DisposeAsync();
        }

        _senders.Clear();

        // Dispose the client (closes the underlying AMQP connection)
        await _client.DisposeAsync();

        _senderLock.Dispose();
    }
}
