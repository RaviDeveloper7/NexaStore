// OrderPlacedConsumerService.cs — consumes "order-placed" Service Bus messages
// using the Azure Service Bus SDK directly via ServiceBusProcessor.
//
// IN: This is the production pattern for Service Bus consumption in .NET.
// BackgroundService is the ASP.NET Core abstraction for long-running background work.
// It starts when the application starts and stops when the application shuts down.
// The ServiceBusProcessor runs a message pump internally — it maintains a
// persistent AMQP link to the subscription and pushes messages to your handler
// as they arrive. No polling, no timer — pure event-driven push delivery.
//
// IN: Why BackgroundService over Azure Functions ServiceBusTrigger?
// Functions ServiceBusTrigger: Functions host owns the processor lifecycle.
//   You have less control. Requires the Functions runtime.
//   Fine for serverless/consumption plan deployments.
// BackgroundService + ServiceBusProcessor: YOUR code owns everything.
//   Full control over concurrency, prefetch count, lock renewal, retry logic.
//   No Functions runtime dependency. Runs inside your existing API/Worker process.
//   Standard in production .NET microservices and Azure App Service deployments.
//
// IN: BackgroundService is Singleton — it lives for the entire app lifetime.
// But it needs Scoped services (UserManager, IEmailService).
// The solution is IServiceScopeFactory — create a new DI scope per message,
// resolve scoped services within that scope, dispose the scope when done.
// This is the standard pattern for Singleton services consuming Scoped dependencies.
// Never inject IDbContext or UserManager directly into a Singleton — they are Scoped
// and their reuse across requests causes data corruption and threading bugs.

using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NexaStore.Application.Common.Interfaces.Services;
using NexaStore.Domain.Events;
using NexaStore.Identity.Models;

namespace NexaStore.Infrastructure.MessageBus;

public class OrderPlacedConsumerService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ServiceBusClient _client;
    private readonly ServiceBusSettings _settings;
    private readonly ILogger<OrderPlacedConsumerService> _logger;

    // The processor — owns the AMQP link and message pump
    private ServiceBusProcessor? _processor;

    public OrderPlacedConsumerService(
        IServiceScopeFactory scopeFactory,
        ServiceBusClient client,
        IOptions<ServiceBusSettings> settings,
        ILogger<OrderPlacedConsumerService> logger)
    {
        _scopeFactory = scopeFactory;
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    // =========================================================================
    // STARTUP — called once when the application starts
    // =========================================================================

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Guard — if Service Bus is not configured, log and exit gracefully.
        // The rest of the application starts normally. This is important for
        // local development where Service Bus credentials may not be set.
        if (string.IsNullOrWhiteSpace(_settings.ConnectionString))
        {
            _logger.LogWarning(
                "ServiceBus:ConnectionString is not configured. " +
                "OrderPlacedConsumerService will not start. " +
                "Order confirmation emails will not be sent.");
            return;
        }

        // IN: ServiceBusProcessorOptions controls how the processor behaves.
        _processor = _client.CreateProcessor(
            topicName: _settings.OrderPlacedTopic,
            subscriptionName: "order-notifications",
            options: new ServiceBusProcessorOptions
            {
                // IN: MaxConcurrentCalls = 1 — process one message at a time.
                // This guarantees ordered processing within this subscription.
                // For higher throughput with acceptable out-of-order risk, increase this.
                // For our email use case, ordering matters less — but starting at 1
                // is the safe default. Tune based on load testing.
                MaxConcurrentCalls = 1,

                // IN: AutoCompleteMessages = false — WE call CompleteMessageAsync
                // explicitly after successful processing.
                // If true, the SDK completes the message as soon as your handler returns,
                // even if it threw an exception internally and you swallowed it.
                // false = we control exactly when a message is considered "done".
                // This is the correct production setting — always use false.
                AutoCompleteMessages = false,

                // IN: MaxAutoLockRenewalDuration — how long the SDK will automatically
                // renew the message lock before your handler finishes.
                // Service Bus locks a message when it's delivered — if you don't
                // complete/abandon it before the lock expires, it becomes available
                // for redelivery. For email sending that might take a few seconds,
                // 5 minutes is more than sufficient.
                MaxAutoLockRenewalDuration = TimeSpan.FromMinutes(5),

                // IN: PrefetchCount — number of messages the SDK fetches in advance
                // from Service Bus and holds in a local buffer.
                // 0 = fetch on demand (lower latency for sparse traffic).
                // Higher values = better throughput for high-volume scenarios.
                // 0 is correct for order confirmations — volume is low.
                PrefetchCount = 0
            });

        // Register the message handler — called for every message received
        _processor.ProcessMessageAsync += ProcessMessageAsync;

        // Register the error handler — called when the processor itself has an error
        // (NOT when your handler throws — that's handled inside ProcessMessageAsync)
        _processor.ProcessErrorAsync += ProcessErrorAsync;

        _logger.LogInformation(
            "OrderPlacedConsumerService starting. " +
            "Listening on topic '{Topic}', subscription 'order-notifications'.",
            _settings.OrderPlacedTopic);

        // Start the message pump — this returns immediately.
        // The processor runs on background threads managed by the SDK.
        await _processor.StartProcessingAsync(stoppingToken);

        // IN: Keep ExecuteAsync alive until the application is shutting down.
        // Task.Delay(Timeout.Infinite, stoppingToken) blocks until stoppingToken
        // is cancelled (triggered by app shutdown). At that point we fall through
        // to the finally block and stop the processor gracefully.
        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Expected — stoppingToken was cancelled. Normal shutdown path.
        }
        finally
        {
            await StopProcessorAsync();
        }
    }

    // =========================================================================
    // MESSAGE HANDLER — called for every message delivered by Service Bus
    // =========================================================================

    private async Task ProcessMessageAsync(ProcessMessageEventArgs args)
    {
        var messageId = args.Message.MessageId;
        _logger.LogInformation(
            "OrderPlacedConsumerService received message. MessageId={MessageId}",
            messageId);

        // IN: Create a fresh DI scope for this message.
        // Each message gets its own scope — its own DbContext instance,
        // its own UserManager instance, its own IEmailService instance.
        // Disposing the scope at the end cleans up all scoped resources.
        // This prevents state leaking between messages.
        await using var scope = _scopeFactory.CreateAsyncScope();

        try
        {
            // ================================================================
            // STEP 1: Deserialise the message payload
            // ================================================================

            OrderPlacedEvent? orderPlacedEvent;

            try
            {
                orderPlacedEvent = JsonSerializer.Deserialize<OrderPlacedEvent>(
                    args.Message.Body.ToString(),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex,
                    "Failed to deserialise OrderPlacedEvent. MessageId={MessageId}. " +
                    "Dead-lettering message — it can never be processed.",
                    messageId);

                // IN: DeadLetterMessageAsync — sends the message to the Dead Letter
                // Queue (DLQ). It will not be redelivered automatically.
                // Use this for permanently unprocessable messages:
                // malformed JSON, unknown schema, missing required fields.
                // Operations teams can inspect and replay DLQ messages manually.
                // Never dead-letter for transient errors (network, DB timeout) —
                // those should be abandoned for retry.
                await args.DeadLetterMessageAsync(
                    args.Message,
                    deadLetterReason: "DeserializationFailure",
                    deadLetterErrorDescription: ex.Message,
                    args.CancellationToken);
                return;
            }

            if (orderPlacedEvent is null)
            {
                _logger.LogError(
                    "Deserialised OrderPlacedEvent is null. MessageId={MessageId}. " +
                    "Dead-lettering message.",
                    messageId);

                await args.DeadLetterMessageAsync(
                    args.Message,
                    deadLetterReason: "NullPayload",
                    deadLetterErrorDescription: "Message body deserialised to null.",
                    args.CancellationToken);
                return;
            }

            // ================================================================
            // STEP 2: Load customer details using a scoped UserManager
            // ================================================================

            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

            var user = await userManager.FindByIdAsync(orderPlacedEvent.CustomerId.ToString());

            if (user is null)
            {
                _logger.LogWarning(
                    "Customer not found for CustomerId={CustomerId}. " +
                    "OrderId={OrderId}. MessageId={MessageId}. " +
                    "Dead-lettering — cannot send email without customer record.",
                    orderPlacedEvent.CustomerId,
                    orderPlacedEvent.OrderId,
                    messageId);

                // IN: Dead-letter here too — if the customer doesn't exist,
                // retrying won't help. The account was deleted or never created.
                await args.DeadLetterMessageAsync(
                    args.Message,
                    deadLetterReason: "CustomerNotFound",
                    deadLetterErrorDescription: $"CustomerId {orderPlacedEvent.CustomerId} not found.",
                    args.CancellationToken);
                return;
            }

            if (string.IsNullOrWhiteSpace(user.Email))
            {
                _logger.LogWarning(
                    "Customer {CustomerId} has no email. " +
                    "OrderId={OrderId}. MessageId={MessageId}. Dead-lettering.",
                    orderPlacedEvent.CustomerId,
                    orderPlacedEvent.OrderId,
                    messageId);

                await args.DeadLetterMessageAsync(
                    args.Message,
                    deadLetterReason: "NoEmailAddress",
                    deadLetterErrorDescription: "Customer has no email address.",
                    args.CancellationToken);
                return;
            }

            // ================================================================
            // STEP 3: Send the confirmation email using a scoped IEmailService
            // ================================================================

            var emailService = scope.ServiceProvider
                .GetRequiredService<IEmailService>();

            await emailService.SendOrderConfirmationAsync(
                toEmail: user.Email,
                customerName: user.FullName,
                orderId: orderPlacedEvent.OrderId,
                totalAmount: orderPlacedEvent.TotalAmount,
                args.CancellationToken);

            // ================================================================
            // STEP 4: Complete the message — remove it from the subscription
            // ================================================================

            // IN: CompleteMessageAsync tells Service Bus:
            // "I have successfully processed this message. Remove it."
            // Only call this AFTER all processing succeeds.
            // If any step above throws, we fall into the catch block below
            // and abandon the message for retry instead.
            await args.CompleteMessageAsync(args.Message, args.CancellationToken);

            _logger.LogInformation(
                "Order confirmation email sent and message completed. " +
                "OrderId={OrderId}, CustomerEmail={Email}, MessageId={MessageId}",
                orderPlacedEvent.OrderId,
                user.Email,
                messageId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to process OrderPlacedEvent. MessageId={MessageId}. " +
                "Abandoning message for retry.",
                messageId);

            // IN: AbandonMessageAsync tells Service Bus:
            // "I failed to process this message. Make it available for redelivery."
            // The delivery count increments by 1.
            // When delivery count reaches MaxDeliveryCount (configured on subscription,
            // default 10), Service Bus automatically moves it to the Dead Letter Queue.
            // Use Abandon for TRANSIENT failures: DB timeout, email provider down, etc.
            // Use DeadLetter for PERMANENT failures: bad JSON, missing data, etc.
            try
            {
                await args.AbandonMessageAsync(args.Message, cancellationToken: args.CancellationToken);
            }
            catch (Exception abandonEx)
            {
                // Abandoning itself failed (e.g. message lock expired).
                // Log and move on — Service Bus will redeliver when the lock expires.
                _logger.LogError(abandonEx,
                    "Failed to abandon message MessageId={MessageId}. " +
                    "Lock may have expired — Service Bus will redeliver automatically.",
                    messageId);
            }
        }
    }

    // =========================================================================
    // ERROR HANDLER — called for processor-level errors (not message handler errors)
    // =========================================================================

    private Task ProcessErrorAsync(ProcessErrorEventArgs args)
    {
        // IN: This handler fires for errors in the processor itself:
        // lost connection to Service Bus, AMQP protocol errors, auth failures.
        // NOT for exceptions thrown inside ProcessMessageAsync — those are caught
        // by the try/catch in the message handler above.
        // The SDK handles reconnection automatically — this is for logging/alerting.
        _logger.LogError(args.Exception,
            "Service Bus processor error. " +
            "Source={ErrorSource}, EntityPath={EntityPath}, Namespace={Namespace}",
            args.ErrorSource,
            args.EntityPath,
            args.FullyQualifiedNamespace);

        return Task.CompletedTask;
    }

    // =========================================================================
    // SHUTDOWN — graceful stop
    // =========================================================================

    private async Task StopProcessorAsync()
    {
        if (_processor is null) return;

        try
        {
            // IN: StopProcessingAsync — signals the processor to stop accepting
            // new messages. It waits for any in-flight message handler to complete
            // before returning. Graceful drain — no messages are abandoned mid-processing.
            await _processor.StopProcessingAsync();
            _logger.LogInformation(
                "OrderPlacedConsumerService stopped gracefully.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error stopping OrderPlacedConsumerService processor.");
        }
        finally
        {
            await _processor.DisposeAsync();
        }
    }
}
