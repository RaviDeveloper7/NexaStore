// IMessageBusPublisher.cs — contract for publishing messages to Azure Service Bus.
// INTERVIEW: Only the OutboxProcessorFunction calls this — handlers never publish
// directly to Service Bus. That's the entire point of the Outbox Pattern.
// Direct publish from handler: Order saved + publish fails = lost event.
// Outbox pattern: Order + OutboxMessage saved atomically, processor publishes later.

namespace NexaStore.Application.Common.Interfaces.Services;

public interface IMessageBusPublisher
{
    // Publish a message to a specific Service Bus topic.
    // topicName: "order-placed", "order-cancelled", "payment-completed"
    // message: the serialized payload (already JSON from the OutboxMessage)
    // INTERVIEW: Taking a raw string keeps this interface decoupled from
    // specific message types — the publisher doesn't care what the payload is.
    Task PublishAsync(
        string topicName,
        string message,
        CancellationToken cancellationToken = default);
}
