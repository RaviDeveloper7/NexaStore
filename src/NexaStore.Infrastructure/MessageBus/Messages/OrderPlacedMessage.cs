namespace NexaStore.Infrastructure.MessageBus.Messages;

public class OrderPlacedMessage
{
    // IN: MessageId enables idempotent processing at consumer side.
    public Guid MessageId { get; set; } = Guid.NewGuid();

    public Guid OrderId { get; set; }
    public Guid CustomerId { get; set; }
    public decimal TotalAmount { get; set; }
    public DateTime OccurredOn { get; set; }
}
