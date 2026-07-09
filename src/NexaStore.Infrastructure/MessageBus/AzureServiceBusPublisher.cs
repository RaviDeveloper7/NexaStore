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

    // IN: Cache one sender per topic to avoid creating expensive AMQP links repeatedly.
    private readonly Dictionary<string, ServiceBusSender> _senders = new();

    // IN: SemaphoreSlim prevents duplicate sender creation under concurrent requests.
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
            // IN: BinaryData.FromString encodes JSON as UTF-8; ContentType identifies format.
            var serviceBusMessage = new ServiceBusMessage(
                BinaryData.FromString(message))
            {
                ContentType = "application/json",

                // IN: MessageId enables duplicate detection at Service Bus level.
                MessageId = Guid.NewGuid().ToString(),

                // Subject helps consumers filter messages without deserialisation.
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
            // IN: Transient errors are retried by OutboxProcessorFunction.
            _logger.LogWarning(ex,
                "Transient Service Bus error publishing to '{Topic}'. " +
                "OutboxMessage will be retried on next processor execution.",
                topicName);
            throw;
        }
        catch (ServiceBusException ex)
        {
            // Non-transient Service Bus error — requires configuration review.
            _logger.LogError(ex,
                "Non-transient Service Bus error publishing to '{Topic}'. " +
                "Check ServiceBus configuration and topic existence.",
                topicName);
            throw;
        }
    }

    // IN: Double-check locking pattern prevents duplicate sender creation.
    private async Task<ServiceBusSender> GetOrCreateSenderAsync(string topicName)
    {
        // Fast path — sender already exists
        if (_senders.TryGetValue(topicName, out var existingSender))
            return existingSender;

        // Slow path — create sender with lock
        await _senderLock.WaitAsync();
        try
        {
            // IN: Double-check after lock acquisition to prevent duplicate creation under concurrency.
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

    // IN: IAsyncDisposable for proper AMQP link cleanup on shutdown.
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
