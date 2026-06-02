// ServiceBusSettings.cs — strongly-typed Service Bus configuration.
// IN: Three topics — one per domain event type.
// Azure Service Bus Topics + Subscriptions = publish-once, consume-many.
// One topic can have multiple subscriptions — each subscription gets its own
// copy of the message. Adding a new consumer = adding a new subscription.
// No publisher changes needed. This is the fan-out pattern.
//
// Topic → Subscription mapping:
// order-placed       → order-notifications  (OrderPlacedConsumerFunction — sends email)
// order-cancelled    → order-refunds        (future: trigger refund processing)
// payment-completed  → order-fulfillment    (future: trigger warehouse fulfilment)

namespace NexaStore.Infrastructure.MessageBus;

public class ServiceBusSettings
{
    public const string SectionName = "ServiceBus";

    // Fully-qualified Service Bus namespace connection string.
    // Format: "Endpoint=sb://nexastore.servicebus.windows.net/;SharedAccessKeyName=...;SharedAccessKey=..."
    // IN: In production, use Managed Identity instead of connection strings.
    // Managed Identity = no secrets to rotate, no connection string in Key Vault.
    // For a portfolio project, connection string is acceptable and simpler to demo.
    public string ConnectionString { get; set; } = string.Empty;

    // Topic names — must match exactly what's configured in Azure Portal
    public string OrderPlacedTopic { get; set; } = "order-placed";
    public string OrderCancelledTopic { get; set; } = "order-cancelled";
    public string PaymentCompletedTopic { get; set; } = "payment-completed";
}
