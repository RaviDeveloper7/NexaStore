// OrderPlacedMessage.cs — the Service Bus message contract for order placement.
// IN: Why a separate Message class instead of serialising OrderPlacedEvent directly?
//
// OrderPlacedEvent is a DOMAIN event — it lives in NexaStore.Domain.
// It may evolve with domain logic (new properties, changed semantics).
//
// OrderPlacedMessage is a MESSAGE CONTRACT — it is the agreed serialisation
// format between the publisher (OutboxProcessorFunction) and the consumer
// (OrderPlacedConsumerFunction, or any future subscriber).
//
// These are different concerns that can evolve independently:
// - Domain event changes: internal, any version
// - Message contract changes: external, requires versioning and backward compatibility
//
// IN: Message contracts should be versioned (v1, v2) when breaking changes occur.
// A consumer running v1 should still process v1 messages even after the publisher
// moves to v2. This is the message schema evolution problem — separating
// domain events from message contracts makes it manageable.

namespace NexaStore.Infrastructure.MessageBus.Messages;

public class OrderPlacedMessage
{
    // IN: MessageId for idempotency at the consumer side.
    // If Service Bus delivers the message twice (at-least-once guarantee),
    // the consumer checks MessageId against a processed-IDs store and skips duplicates.
    // Without MessageId, duplicate deliveries cause duplicate emails.
    public Guid MessageId { get; set; } = Guid.NewGuid();

    public Guid OrderId { get; set; }
    public Guid CustomerId { get; set; }
    public decimal TotalAmount { get; set; }

    // Timestamp when the event occurred — for ordering and audit
    public DateTime OccurredOn { get; set; }
}
