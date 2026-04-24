// OrderCancelledEvent.cs — raised when an Order is cancelled.
// Consumed by the "order-cancelled" Service Bus topic subscription.
// Used to trigger refund processing and inventory restoration.

namespace NexaStore.Domain.Events;

public class OrderCancelledEvent : IDomainEvent
{
    public Guid OrderId { get; init; }

    public Guid CustomerId { get; init; }

    // The reason for cancellation — important for audit trail
    // e.g. "Customer requested", "Payment failed", "Expired after 24 hours"
    public string Reason { get; init; }

    public DateTime OccurredOn { get; init; }

    public OrderCancelledEvent(Guid orderId, Guid customerId, string reason)
    {
        OrderId = orderId;
        CustomerId = customerId;
        Reason = reason;
        OccurredOn = DateTime.UtcNow;
    }
}
