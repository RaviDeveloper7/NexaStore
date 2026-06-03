// OrderPlacedConsumerFunction.cs — consumes "order-placed" Service Bus messages
// and sends order confirmation emails to customers.
//
// IN: This function proves the full publish/consume cycle:
// - The API publishes domain events via the Outbox Pattern
// - This function CONSUMES those events from Service Bus
// - Having both publisher AND consumer in one codebase demonstrates you
//   understand the entire event-driven pipeline, not just one side of it.
//
// IN: ServiceBusTrigger vs TimerTrigger:
// TimerTrigger (OutboxProcessor, OrderExpiry): poll-based, runs on a schedule.
// ServiceBusTrigger (this function): push-based, fires IMMEDIATELY when a
// message arrives on the subscription. No polling delay.
// Use TimerTrigger for jobs that scan the DB.
// Use ServiceBusTrigger for jobs that react to external events.
//
// IN: Why a Function instead of an in-process MediatR notification handler?
// In-process handler: runs in the same HTTP request, same process, same thread.
//   If the handler throws, the entire request fails and rolls back.
//   If the API scales to 3 instances, all 3 process the event simultaneously.
// ServiceBusTrigger Function: runs in a separate process, separate lifecycle.
//   Failure is isolated — order placement still succeeds even if email fails.
//   Service Bus delivers to exactly one consumer instance (competing consumers).
//   Independent scaling — email processing scales separately from the API.

using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using NexaStore.Application.Common.Interfaces.Services;
using NexaStore.Domain.Events;
using NexaStore.Identity.Models;

namespace NexaStore.Functions;

public class OrderPlacedConsumerFunction
{
    private readonly IEmailService _emailService;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<OrderPlacedConsumerFunction> _logger;

    // IN: UserManager<ApplicationUser> injected directly into the Function.
    // This is the one place the Functions project touches the Identity layer —
    // only to look up the customer's email address for the confirmation email.
    // The Function has no concept of "current user" or JWT — it acts as the system.
    //
    // IN: Why not pass email in the OutboxMessage/Service Bus message directly?
    // Option A (current): store CustomerId in message, look up email at send time.
    //   Pros: message is small, email is always current (not stale from order time)
    //   Cons: one extra DB call per message
    // Option B: store email in the message payload.
    //   Pros: no DB call needed
    //   Cons: if customer changes email between order and delivery, wrong email is used.
    //   Also: PII (email address) travelling through Service Bus requires
    //   encryption-in-transit to be verified and may have compliance implications.
    // Option A is the correct production choice.
    public OrderPlacedConsumerFunction(
        IEmailService emailService,
        UserManager<ApplicationUser> userManager,
        ILogger<OrderPlacedConsumerFunction> logger)
    {
        _emailService = emailService;
        _userManager = userManager;
        _logger = logger;
    }

    // IN: ServiceBusTrigger parameters:
    // topicName: "order-placed" — must match ServiceBusSettings.OrderPlacedTopic
    //   and the topic name used by AzureServiceBusPublisher.
    // subscriptionName: "order-notifications" — the subscription created in Azure Portal.
    //   One topic can have MULTIPLE subscriptions. Each subscription receives
    //   its own copy of every message. Adding a new consumer = new subscription only.
    //   No publisher changes. This is the fan-out pattern.
    // connection: "ServiceBus:ConnectionString" — the appsettings key for the
    //   connection string. The Functions runtime resolves this from configuration.
    //   Matches the key structure in local.settings.json and Azure App Settings.
    //
    // IN: The message body (string messageBody) is the raw JSON payload
    // from OutboxMessage.Payload — the serialised OrderPlacedEvent.
    // We deserialise it here to get the structured event data.
    [Function(nameof(OrderPlacedConsumerFunction))]
    public async Task Run(
        [ServiceBusTrigger(
            "order-placed",
            "order-notifications",
            Connection = "ServiceBus:ConnectionString")]
        string            messageBody,
        FunctionContext context,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "OrderPlacedConsumerFunction triggered. Processing message.");

        // =====================================================================
        // STEP 1: Deserialise the message payload
        // =====================================================================

        OrderPlacedEvent? orderPlacedEvent;

        try
        {
            // IN: The Payload in OutboxMessage is JsonSerializer.Serialize(orderPlacedEvent).
            // We deserialise back to OrderPlacedEvent using System.Text.Json.
            // PropertyNameCaseInsensitive: true handles any case differences between
            // serialiser and deserialiser configurations.
            orderPlacedEvent = JsonSerializer.Deserialize<OrderPlacedEvent>(
                messageBody,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException ex)
        {
            // IN: Malformed JSON — this message can never be processed.
            // Log as Error so it appears in Application Insights for investigation.
            // Do NOT throw — throwing causes Service Bus to retry the message.
            // Retrying a permanently malformed message wastes resources and
            // eventually dead-letters it anyway. Log and return to dead-letter immediately.
            _logger.LogError(ex,
                "Failed to deserialise OrderPlacedEvent from message body. " +
                "Message will be dead-lettered. Body: {Body}",
                messageBody);

            // IN: Returning without throwing signals to Service Bus that the
            // message was "processed" (even though it failed). The message is
            // completed and removed from the subscription.
            // To send to dead-letter instead: throw an exception — Service Bus
            // will retry up to MaxDeliveryCount times then dead-letter automatically.
            // For a permanently bad message, completing it (return) is acceptable.
            // For a transiently bad message, throw to trigger retry.
            return;
        }

        if (orderPlacedEvent is null)
        {
            _logger.LogError(
                "Deserialised OrderPlacedEvent is null. " +
                "Skipping message. Body: {Body}",
                messageBody);
            return;
        }

        _logger.LogInformation(
            "Processing OrderPlacedEvent: OrderId={OrderId}, CustomerId={CustomerId}, " +
            "TotalAmount={TotalAmount}",
            orderPlacedEvent.OrderId,
            orderPlacedEvent.CustomerId,
            orderPlacedEvent.TotalAmount);

        // =====================================================================
        // STEP 2: Load customer details
        // =====================================================================

        // IN: CustomerId in the event is a Guid (our domain type).
        // UserManager.FindByIdAsync expects a string (Identity's type).
        // Convert at the boundary — domain stays Guid, Identity stays string.
        var user = await _userManager
            .FindByIdAsync(orderPlacedEvent.CustomerId.ToString());

        if (user is null)
        {
            // IN: Customer not found — account may have been deleted after order.
            // Log as Warning (not Error) — this is an edge case, not a bug.
            // We cannot send the email without an address — log and complete the message.
            // IN production: consider sending to a fallback admin address or
            // creating a support ticket for manual follow-up.
            _logger.LogWarning(
                "Customer not found for CustomerId={CustomerId}. " +
                "OrderId={OrderId}. Cannot send confirmation email.",
                orderPlacedEvent.CustomerId,
                orderPlacedEvent.OrderId);
            return;
        }

        if (string.IsNullOrWhiteSpace(user.Email))
        {
            _logger.LogWarning(
                "Customer {CustomerId} has no email address. " +
                "Cannot send order confirmation for OrderId={OrderId}.",
                orderPlacedEvent.CustomerId,
                orderPlacedEvent.OrderId);
            return;
        }

        // =====================================================================
        // STEP 3: Send the confirmation email
        // =====================================================================

        // IN: EmailService handles its own exceptions internally —
        // it logs and returns without throwing even on SendGrid failure.
        // We wrap in try/catch here as a second safety net.
        // If email sending fails here, we log the error and return —
        // the Service Bus message is completed (not retried).
        //
        // IN: Should email failure cause a retry?
        // If the email provider (SendGrid) is temporarily down:
        //   Throw → Service Bus retries up to MaxDeliveryCount times → retry is correct.
        // If the customer has no email / account deleted:
        //   Return → complete the message → no retry needed.
        // EmailService.SendOrderConfirmationAsync only throws on unexpected errors,
        // not on missing API key (it logs and returns). So throws here are genuine
        // transient failures worth retrying.
        try
        {
            await _emailService.SendOrderConfirmationAsync(
                toEmail: user.Email,
                customerName: user.FullName,
                orderId: orderPlacedEvent.OrderId,
                totalAmount: orderPlacedEvent.TotalAmount,
                cancellationToken);

            _logger.LogInformation(
                "Order confirmation email sent successfully. " +
                "OrderId={OrderId}, CustomerEmail={Email}",
                orderPlacedEvent.OrderId,
                user.Email);
        }
        catch (Exception ex)
        {
            // IN: Unexpected email failure — throw to trigger Service Bus retry.
            // Service Bus will retry up to MaxDeliveryCount (default 10) times.
            // After MaxDeliveryCount failures, the message is dead-lettered.
            // Dead-lettered messages can be inspected and replayed from the
            // Azure Portal or via Service Bus Explorer — no emails are permanently lost.
            _logger.LogError(ex,
                "Unexpected error sending confirmation email. " +
                "OrderId={OrderId}, CustomerEmail={Email}. " +
                "Service Bus will retry this message.",
                orderPlacedEvent.OrderId,
                user.Email);

            // Re-throw — signals Service Bus to NOT complete the message,
            // making it available for retry on the subscription.
            throw;
        }
    }
}
