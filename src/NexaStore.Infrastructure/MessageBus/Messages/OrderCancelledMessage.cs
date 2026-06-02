// OrderCancelledMessage.cs — Service Bus message contract for order cancellation.
// Consumed by the order-refunds subscription (future refund processing).

namespace NexaStore.Infrastructure.MessageBus.Messages;

public class OrderCancelledMessage
{
    public Guid MessageId { get; set; } = Guid.NewGuid();
    public Guid OrderId { get; set; }
    public Guid CustomerId { get; set; }

    // Reason is included in the message — consumer can use it in the
    // cancellation email and refund notes without an extra DB lookup.
    public string Reason { get; set; } = string.Empty;
    public DateTime OccurredOn { get; set; }
}
