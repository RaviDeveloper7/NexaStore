using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using NexaStore.Application.Common.Interfaces.Services;

namespace NexaStore.Functions;

public class OutboxProcessorFunction
{
    private readonly IOutboxRepository _outboxRepository;
    private readonly IMessageBusPublisher _messageBusPublisher;
    private readonly ILogger<OutboxProcessorFunction> _logger;

    public OutboxProcessorFunction(
        IOutboxRepository outboxRepository,
        IMessageBusPublisher messageBusPublisher,
        ILogger<OutboxProcessorFunction> logger)
    {
        _outboxRepository = outboxRepository;
        _messageBusPublisher = messageBusPublisher;
        _logger = logger;
    }

    [Function(nameof(OutboxProcessorFunction))]
    public async Task Run([TimerTrigger("*/10 * * * * *", RunOnStartup = true)]
        TimerInfo timerInfo,CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "OutboxProcessorFunction triggered at {UtcNow}. " +
            "IsPastDue: {IsPastDue}",
            DateTime.UtcNow,
            timerInfo.IsPastDue);

        if (timerInfo.IsPastDue)
        {
            _logger.LogWarning(
                "OutboxProcessorFunction is running late. " +
                "Unprocessed messages may have been waiting longer than expected.");
        }

        // Fetch unprocessed messages (batch of 50)
        IReadOnlyList<Domain.Entities.OutboxMessage> messages;

        try
        {
            messages = await _outboxRepository
                .GetUnprocessedAsync(batchSize: 50, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to fetch unprocessed OutboxMessages from database. " +
                "Will retry on next timer execution.");
            return;
        }

        if (messages.Count == 0)
        {
            _logger.LogDebug(
                "OutboxProcessorFunction: no unprocessed messages found.");
            return;
        }

        _logger.LogInformation(
            "OutboxProcessorFunction: processing {Count} messages.",
            messages.Count);

        // =====================================================================
        // STEP 2: Process each message — publish then mark processed
        // =====================================================================

        var successCount = 0;
        var failureCount = 0;

        foreach (var message in messages)
        {
            // IN: Process messages one-by-one, not in parallel.
            // Parallel publishing risks out-of-order delivery to Service Bus.
            // For an order: OrderPlacedEvent MUST arrive before OrderCancelledEvent.
            // Sequential processing preserves the CreatedAt ordering from Step 1.
            //
            // IN: Why not use Task.WhenAll for performance?
            // Service Bus topics do not guarantee ordering across parallel sends.
            // The trade-off: slightly lower throughput (50 sequential publishes per 10s)
            // vs correct ordering. For NexaStore's volume, sequential is correct.
            // High-volume systems: use Service Bus sessions for ordered delivery,
            // then parallel publish is safe within a session.
            try
            {
                // Determine the target topic from the message Type field.
                // IN: The Type field is the fully-qualified event class name:
                // "NexaStore.Domain.Events.OrderPlacedEvent"
                // We map this to the correct Service Bus topic name.
                var topicName = ResolveTopicName(message.Type);

                if (topicName is null)
                {
                    // Unknown event type — mark as processed to prevent infinite retry loop.
                    // IN: An unrecognised type means a bug in the publisher (handler).
                    // Logging as Error here surfaces it in Application Insights for triage.
                    // Marking processed prevents it from blocking all subsequent messages.
                    _logger.LogError(
                        "Unknown OutboxMessage type '{Type}' (Id: {MessageId}). " +
                        "Marking as processed to prevent retry loop.",
                        message.Type,
                        message.Id);

                    await _outboxRepository
                        .MarkAsProcessedAsync(message.Id, cancellationToken);

                    failureCount++;
                    continue;
                }

                // Publish the JSON payload to the resolved Service Bus topic
                await _messageBusPublisher.PublishAsync(
                    topicName,
                    message.Payload,
                    cancellationToken);

                // Mark as processed ONLY after successful publish.
                // IN: This ordering is critical.
                // Mark BEFORE publish: message marked processed but publish fails
                //   → event permanently lost. Never acceptable.
                // Mark AFTER publish: publish succeeds, mark fails
                //   → message republished on next tick → consumer receives duplicate.
                //   → consumer must be idempotent (handle duplicates).
                //   → at-least-once delivery: acceptable trade-off.
                // Duplicates are recoverable. Lost events are not.
                await _outboxRepository
                    .MarkAsProcessedAsync(message.Id, cancellationToken);

                successCount++;

                _logger.LogDebug(
                    "OutboxMessage processed: Id={MessageId}, Type={Type}, Topic={Topic}",
                    message.Id,
                    message.Type,
                    topicName);
            }
            catch (Exception ex)
            {
                // IN: Per-message failure — log and CONTINUE to the next message.
                // Do NOT break out of the loop on a single failure.
                // One bad message should not block all subsequent messages.
                // The failed message's ProcessedAt remains null — it will be
                // retried on the next timer execution automatically.
                _logger.LogError(ex,
                    "Failed to process OutboxMessage Id={MessageId}, Type={Type}. " +
                    "Will retry on next timer execution.",
                    message.Id,
                    message.Type);

                failureCount++;
            }
        }

        _logger.LogInformation(
            "OutboxProcessorFunction completed: {Success} succeeded, {Failed} failed " +
            "out of {Total} messages.",
            successCount,
            failureCount,
            messages.Count);
    }

    // =========================================================================
    // PRIVATE: Resolve event type name to Service Bus topic name
    // =========================================================================

    // IN: Maps the fully-qualified .NET type name stored in OutboxMessage.Type
    // to the Azure Service Bus topic name.
    // This mapping lives here — not in a config file — because it is a
    // code-level contract between the publisher and the topics.
    // A config-driven mapping would be more flexible but adds complexity for
    // no real benefit in a single-service system.
    private static string? ResolveTopicName(string messageType) =>
        messageType switch
        {
            // IN: Suffix matching (.EndsWith) instead of full type name matching.
            // Full name: "NexaStore.Domain.Events.OrderPlacedEvent"
            // If the namespace changes (refactoring), full match breaks silently.
            // Suffix matching on the class name is more resilient to refactoring.
            var t when t.EndsWith(nameof(Domain.Events.OrderPlacedEvent))
                => "order-placed",

            var t when t.EndsWith(nameof(Domain.Events.OrderCancelledEvent))
                => "order-cancelled",

            // Unknown type — caller handles null
            _ => null
        };
}
