using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NexaStore.Application.Common.Interfaces.Services;
using SendGrid;
using SendGrid.Helpers.Mail;

namespace NexaStore.Infrastructure.Mail;

public class EmailService : IEmailService
{
    private readonly EmailSettings _settings;
    private readonly ILogger<EmailService> _logger;

    public EmailService(
        IOptions<EmailSettings> settings,
        ILogger<EmailService> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task SendOrderConfirmationAsync(string toEmail, string customerName,
        Guid orderId, decimal totalAmount, CancellationToken cancellationToken = default)
    {
        var subject = $"Order Confirmed — #{orderId.ToString()[..8].ToUpper()}";

        // IN: Use inline styles for email HTML; external CSS is stripped by most clients.
        var htmlContent = BuildOrderConfirmationHtml(
            customerName, orderId, totalAmount);

        var plainContent =
            $"Hi {customerName},\n\n" +
            $"Your order #{orderId.ToString()[..8].ToUpper()} has been confirmed.\n" +
            $"Total: {totalAmount:C}\n\n" +
            $"Thank you for shopping with NexaStore.\n\n" +
            $"The NexaStore Team";

        await SendEmailAsync(
            toEmail,
            customerName,
            subject,
            htmlContent,
            plainContent,
            cancellationToken);
    }

    // =========================================================================
    // ORDER CANCELLATION
    // =========================================================================

    public async Task SendOrderCancellationAsync(string toEmail, string customerName, Guid orderId,
        string reason, CancellationToken cancellationToken = default)
    {
        var subject = $"Order Cancelled — #{orderId.ToString()[..8].ToUpper()}";

        var htmlContent = BuildOrderCancellationHtml(
            customerName, orderId, reason);

        var plainContent =
            $"Hi {customerName},\n\n" +
            $"Your order #{orderId.ToString()[..8].ToUpper()} has been cancelled.\n" +
            $"Reason: {reason}\n\n" +
            $"If you did not request this cancellation, please contact support.\n\n" +
            $"The NexaStore Team";

        await SendEmailAsync(
            toEmail,
            customerName,
            subject,
            htmlContent,
            plainContent,
            cancellationToken);
    }

    // =========================================================================
    // PRIVATE: Core send method
    // =========================================================================

    private async Task SendEmailAsync(string toEmail, string toName, string subject,
        string htmlContent, string plainContent, CancellationToken cancellationToken)
    {
        // IN: Guard against empty ApiKey — fail with a clear message
        // rather than a cryptic SendGrid 401 response.
        if (string.IsNullOrWhiteSpace(_settings.ApiKey))
        {
            _logger.LogWarning(
                "EmailSettings:ApiKey is not configured. " +
                "Email to '{ToEmail}' with subject '{Subject}' was NOT sent.",
                toEmail, subject);
            // IN: Return without throwing in development when SendGrid isn't configured.
            // The application functions correctly — emails are just skipped.
            // A throw here would break the OrderPlacedConsumerFunction in local dev.
            return;
        }

        try
        {
            var client = new SendGridClient(_settings.ApiKey);
            var from = new EmailAddress(_settings.FromEmail, _settings.FromName);
            var to = new EmailAddress(toEmail, toName);

            var message = MailHelper.CreateSingleEmail(
                from,
                to,
                subject,
                plainContent,    // Plain text fallback — always provide both
                htmlContent);    // HTML version — shown when client supports it

            // IN: SendGrid's async send. CancellationToken not directly supported
            // by the SendGrid SDK — the using clause and timeout are the safeguard.
            // In production, wrap in a Polly retry policy for transient 429/500 errors.
            var response = await client.SendEmailAsync(message, cancellationToken);

            if ((int)response.StatusCode >= 400)
            {
                var body = await response.Body.ReadAsStringAsync(cancellationToken);
                _logger.LogError(
                    "SendGrid returned {StatusCode} for email to '{ToEmail}'. " +
                    "Subject: '{Subject}'. Response: {Body}",
                    (int)response.StatusCode, toEmail, subject, body);
            }
            else
            {
                _logger.LogInformation(
                    "Email sent successfully to '{ToEmail}'. Subject: '{Subject}'. " +
                    "SendGrid StatusCode: {StatusCode}",
                    toEmail, subject, (int)response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            // IN: Email failure must never crash the Azure Function or the API.
            // Log the error and continue — the order is still placed/cancelled.
            // A failed email is a UX problem, not a data integrity problem.
            // The function can be retried via Service Bus dead-letter queue
            // if needed for guaranteed email delivery.
            _logger.LogError(ex,
                "Failed to send email to '{ToEmail}'. Subject: '{Subject}'.",
                toEmail, subject);
        }
    }

    // =========================================================================
    // PRIVATE: HTML builders
    // =========================================================================

    private static string BuildOrderConfirmationHtml(string customerName, Guid orderId, decimal totalAmount)
    {
        return $"""
        <h2>Order Confirmed</h2>
        <p>Hi {customerName},</p>
        <p>Your order <strong>#{orderId.ToString()[..8].ToUpper()}</strong> 
           has been confirmed. Total: <strong>{totalAmount:C}</strong></p>
        <p>Thank you for shopping with NexaStore.</p>
        """;
    }

    private static string BuildOrderCancellationHtml(string customerName,Guid orderId,string reason)
    {
        return $"""
        <h2>Order Cancelled</h2>
        <p>Hi {customerName},</p>
        <p>Your order <strong>#{orderId.ToString()[..8].ToUpper()}</strong> 
           has been cancelled. Reason: {reason}</p>
        <p>Thank you for shopping with NexaStore.</p>
        """;
    }
}
