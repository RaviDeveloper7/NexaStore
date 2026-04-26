// IEmailService.cs — contract for sending emails.
// Defined in Application, implemented in Infrastructure (EmailService.cs).
// INTERVIEW: The handler that consumes OrderPlacedEvent just calls
// IEmailService.SendOrderConfirmationAsync() — it has zero knowledge of
// SMTP, SendGrid, or any email provider. Swap providers by changing Infrastructure only.

namespace NexaStore.Application.Common.Interfaces.Services;

public interface IEmailService
{
    // Sends an order confirmation email to the customer.
    // Called by OrderPlacedConsumerFunction after consuming from Service Bus.
    Task SendOrderConfirmationAsync(
        string toEmail,
        string customerName,
        Guid orderId,
        decimal totalAmount,
        CancellationToken cancellationToken = default);

    // Sends an order cancellation notification.
    // Called by CancelOrderCommandHandler or OrderExpiryFunction.
    Task SendOrderCancellationAsync(
        string toEmail,
        string customerName,
        Guid orderId,
        string reason,
        CancellationToken cancellationToken = default);
}
