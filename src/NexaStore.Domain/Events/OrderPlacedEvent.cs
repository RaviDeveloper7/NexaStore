// OrderPlacedEvent.cs — raised when a new Order is successfully created.
// INTERVIEW: This event crosses a boundary — it leaves the Domain via the Outbox
// and gets published to Azure Service Bus topic "order-placed".
// The OrderPlacedConsumerFunction receives it and sends a confirmation email.
// Domain events should be immutable — all properties are init-only.

namespace NexaStore.Domain.Events;

public class OrderPlacedEvent : IDomainEvent
{
    // The order that was just placed
    public Guid OrderId { get; init; }

    // The customer who placed it — needed by the email consumer
    public Guid CustomerId { get; init; }

    // Total value — useful for analytics and email content
    public decimal TotalAmount { get; init; }

    // INTERVIEW: init-only properties mean this event is immutable once created.
    // Domain events should never be mutated — they are facts that happened.
    public DateTime OccurredOn { get; init; }

    // Constructor enforces all required data is provided at creation
    public OrderPlacedEvent(Guid orderId, Guid customerId, decimal totalAmount)
    {
        OrderId = orderId;
        CustomerId = customerId;
        TotalAmount = totalAmount;

        // Captured at the moment the event is created — UTC always
        // INTERVIEW: Always use UTC in domain logic. Let the UI layer
        // convert to local time. Mixing local time in the domain is a
        // common source of bugs in global systems.
        OccurredOn = DateTime.UtcNow;
    }
}
