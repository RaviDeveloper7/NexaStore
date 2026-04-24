// OutboxMessage.cs — the heart of the Outbox Pattern.
// INTERVIEW: The Outbox Pattern solves the dual-write problem.
// Without it: save Order succeeds but Service Bus publish fails = lost event.
// With it: Order + OutboxMessage are saved in ONE database transaction.
// The OutboxProcessorFunction then reads unprocessed messages and publishes them.
// At-least-once delivery is guaranteed. Idempotency must be handled by consumers.

namespace NexaStore.Domain.Entities;

public class OutboxMessage
{
    // INTERVIEW: OutboxMessage does NOT extend BaseEntity — it has its own
    // simple Id (Guid) and doesn't need UpdatedAt. Keeping it lean.
    public Guid Id { get; set; }

    // The fully-qualified event type name — used by consumer to deserialize
    // e.g. "NexaStore.Domain.Events.OrderPlacedEvent"
    public string Type { get; set; } = string.Empty;

    // JSON-serialized event payload — stored as text in the DB
    // INTERVIEW: Serializing to JSON means the outbox is schema-independent.
    // You can add new event types without changing the OutboxMessage table.
    public string Payload { get; set; } = string.Empty;

    // Set when the OutboxMessage is created (same transaction as the Order)
    public DateTime CreatedAt { get; set; }

    // Null = not yet processed. Set by OutboxProcessorFunction after publishing.
    // INTERVIEW: This is how the processor knows what's been handled.
    // A simple WHERE ProcessedAt IS NULL query fetches pending messages.
    public DateTime? ProcessedAt { get; set; }
}
