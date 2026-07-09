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

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
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
                // IN: MaxConcurrentCalls = 1 ensures ordered processing.
                // Higher values increase throughput but risk out-of-order delivery.
                MaxConcurrentCalls = 1,

                // IN: AutoCompleteMessages = false ensures explicit control.
                // We call CompleteMessageAsync only after successful processing.
                AutoCompleteMessages = false,

                // IN: Renew message lock for up to 5 minutes during processing.
                MaxAutoLockRenewalDuration = TimeSpan.FromMinutes(5),

                // IN: PrefetchCount = 0 fetches messages on demand (lower latency).
                PrefetchCount = 0
            });

        _processor.ProcessMessageAsync += ProcessMessageAsync;

        _processor.ProcessErrorAsync += ProcessErrorAsync;

        _logger.LogInformation(
            "OrderPlacedConsumerService starting. " +
            "Listening on topic '{Topic}', subscription 'order-notifications'.",
            _settings.OrderPlacedTopic);

        // IN: Processor runs asynchronously on background threads managed by the SDK.
        await _processor.StartProcessingAsync(stoppingToken);

        // IN: Keep ExecuteAsync alive until app shutdown; stoppingToken cancellation triggers graceful stop.
        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Expected on application shutdown.
        }
        finally
        {
            await StopProcessorAsync();
        }
    }

    private async Task ProcessMessageAsync(ProcessMessageEventArgs args)
    {
        var messageId = args.Message.MessageId;
        _logger.LogInformation(
            "OrderPlacedConsumerService received message. MessageId={MessageId}",
            messageId);

        // IN: Each message gets a fresh DI scope to prevent state leakage between messages.
        await using var scope = _scopeFactory.CreateAsyncScope();

        try
        {
            // Deserialise message payload
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

                // IN: DeadLetterMessageAsync sends unprocessable messages to DLQ permanently.
                // Use for permanent errors (malformed JSON, invalid schema).
                // Never dead-letter transient errors (network, DB timeout) — abandon for retry.
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

            // Load customer details using scoped UserManager
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

                // IN: Permanent failure — customer record missing; no recovery possible.
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

            // Send confirmation email using scoped IEmailService
            var emailService = scope.ServiceProvider
                .GetRequiredService<IEmailService>();

            await emailService.SendOrderConfirmationAsync(
                toEmail: user.Email,
                customerName: user.FullName,
                orderId: orderPlacedEvent.OrderId,
                totalAmount: orderPlacedEvent.TotalAmount,
                args.CancellationToken);

            // IN: CompleteMessageAsync removes processed message from subscription.
            // Only call after all processing succeeds; on failure, abandon for retry.
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

            // IN: AbandonMessageAsync makes message available for redelivery after transient failures.
            // Use Abandon for transient errors (DB timeout, service down).
            // Use DeadLetter for permanent failures (bad JSON, missing required data).
            try
            {
                await args.AbandonMessageAsync(args.Message, cancellationToken: args.CancellationToken);
            }
            catch (Exception abandonEx)
            {
                // Log if abandonment fails; Service Bus will redeliver when lock expires.
                _logger.LogError(abandonEx,
                    "Failed to abandon message MessageId={MessageId}. " +
                    "Lock may have expired — Service Bus will redeliver automatically.",
                    messageId);
            }
        }
    }

    private Task ProcessErrorAsync(ProcessErrorEventArgs args)
    {
        // IN: Processor-level errors (connection loss, AMQP failures) — not message handler errors.
        // The SDK handles reconnection automatically; this is for logging/alerting.
        _logger.LogError(args.Exception,
            "Service Bus processor error. " +
            "Source={ErrorSource}, EntityPath={EntityPath}, Namespace={Namespace}",
            args.ErrorSource,
            args.EntityPath,
            args.FullyQualifiedNamespace);

        return Task.CompletedTask;
    }

    private async Task StopProcessorAsync()
    {
        if (_processor is null) return;

        try
        {
            // IN: StopProcessingAsync gracefully drains in-flight messages before returning.
            await _processor.StopProcessingAsync();
            _logger.LogInformation("OrderPlacedConsumerService stopped gracefully.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping OrderPlacedConsumerService processor.");
        }
        finally
        {
            await _processor.DisposeAsync();
        }
    }
}
