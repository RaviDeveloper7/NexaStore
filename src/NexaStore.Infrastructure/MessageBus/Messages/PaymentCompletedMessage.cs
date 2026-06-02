// PaymentCompletedMessage.cs — Service Bus message contract for payment completion.
// Consumed by the order-fulfillment subscription (future warehouse integration).

namespace NexaStore.Infrastructure.MessageBus.Messages;

public class PaymentCompletedMessage
{
    public Guid MessageId { get; set; } = Guid.NewGuid();
    public Guid OrderId { get; set; }
    public Guid PaymentId { get; set; }
    public decimal Amount { get; set; }
    public string Method { get; set; } = string.Empty;
    public DateTime OccurredOn { get; set; }
}
