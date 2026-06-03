// OrderExpiryFunction.cs — automatically cancels Pending orders older than 24 hours.
// IN: This function solves a real business problem:
// A customer places an order but never pays. The order sits in Pending status forever,
// holding a stock reservation. Without expiry, these phantom orders reduce available
// stock for other customers indefinitely.
//
// Solution: every hour, find all Pending orders older than 24 hours and cancel them.
// Stock is restored. The slot is freed for genuine orders.
//
// IN: Why a Function over a scheduled task in the API?
// The API is stateless and scales horizontally — multiple instances run in parallel.
// A scheduled task in the API would fire on ALL instances simultaneously,
// causing duplicate cancellations and race conditions.
// Azure Functions TimerTrigger uses a distributed lock (via Azure Storage)
// to guarantee exactly ONE instance runs the timer at any time.
// This is the correct solution for distributed scheduled jobs.

using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using NexaStore.Application.Common.Interfaces.Persistence;
using NexaStore.Application.Common.Interfaces.Services;
using NexaStore.Domain.Entities;
using NexaStore.Domain.Enums;
using NexaStore.Domain.Events;

namespace NexaStore.Functions;

public class OrderExpiryFunction
{
    private readonly IOrderRepository _orderRepository;
    private readonly IOutboxRepository _outboxRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<OrderExpiryFunction> _logger;

    // IN: Order expiry threshold — how long a Pending order lives before auto-cancel.
    // 24 hours is the NexaStore policy. This should come from configuration
    // in a production system (IOptions<OrderSettings>) so it can be changed
    // without a deployment. Constant here for simplicity.
    private const int ExpiryHours = 24;

    public OrderExpiryFunction(
        IOrderRepository orderRepository,
        IOutboxRepository outboxRepository,
        IUnitOfWork unitOfWork,
        ILogger<OrderExpiryFunction> logger)
    {
        _orderRepository = orderRepository;
        _outboxRepository = outboxRepository;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    // IN: TimerTrigger "0 0 * * * *" = every hour at minute 0.
    // Cron: {second=0} {minute=0} {hour=*} {day=*} {month=*} {weekday=*}
    // Runs at 00:00, 01:00, 02:00 ... 23:00 UTC every day.
    // Hourly is appropriate — 24-hour expiry checked hourly means
    // maximum 1 hour of extra Pending time beyond the policy window.
    [Function(nameof(OrderExpiryFunction))]
    public async Task Run(
        [TimerTrigger("0 0 * * * *", RunOnStartup = false)]
        TimerInfo timerInfo,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "OrderExpiryFunction triggered at {UtcNow}.",
            DateTime.UtcNow);

        // =====================================================================
        // STEP 1: Calculate the expiry cutoff time
        // =====================================================================

        // Any Pending order created BEFORE this time is considered expired.
        // IN: Always use UTC for domain logic — never local time.
        // Azure Functions run in UTC by default. Storing/comparing in UTC
        // eliminates timezone bugs entirely.
        var cutoffTime = DateTime.UtcNow.AddHours(-ExpiryHours);

        _logger.LogInformation(
            "Looking for Pending orders created before {CutoffTime} (older than {Hours}h).",
            cutoffTime,
            ExpiryHours);

        // =====================================================================
        // STEP 2: Load expired orders
        // =====================================================================

        IReadOnlyList<Order> expiredOrders;

        try
        {
            // IN: GetExpiredPendingOrdersAsync returns TRACKED entities.
            // We need tracking because we're about to modify Status and
            // StockQuantity — EF must detect these changes for the UPDATE.
            expiredOrders = await _orderRepository
                .GetExpiredPendingOrdersAsync(cutoffTime, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to fetch expired Pending orders. " +
                "Will retry on next hourly execution.");
            return;
        }

        if (expiredOrders.Count == 0)
        {
            _logger.LogInformation(
                "OrderExpiryFunction: no expired Pending orders found.");
            return;
        }

        _logger.LogInformation(
            "OrderExpiryFunction: found {Count} expired Pending orders to cancel.",
            expiredOrders.Count);

        // =====================================================================
        // STEP 3: Cancel each expired order
        // =====================================================================

        var cancelledCount = 0;
        var failedCount = 0;

        foreach (var order in expiredOrders)
        {
            try
            {
                await CancelExpiredOrderAsync(order, cancellationToken);
                cancelledCount++;

                _logger.LogInformation(
                    "Expired order cancelled: OrderId={OrderId}, " +
                    "CustomerId={CustomerId}, CreatedAt={CreatedAt}",
                    order.Id,
                    order.CustomerId,
                    order.CreatedAt);
            }
            catch (Exception ex)
            {
                // IN: Per-order failure — log and continue.
                // One failure must not stop the entire batch.
                // Failed orders remain Pending and will be retried
                // on the next hourly execution.
                _logger.LogError(ex,
                    "Failed to cancel expired order OrderId={OrderId}. " +
                    "Will retry on next execution.",
                    order.Id);

                failedCount++;
            }
        }

        _logger.LogInformation(
            "OrderExpiryFunction completed: {Cancelled} cancelled, " +
            "{Failed} failed out of {Total} expired orders.",
            cancelledCount,
            failedCount,
            expiredOrders.Count);
    }

    // =========================================================================
    // PRIVATE: Cancel one expired order atomically
    // =========================================================================

    private async Task CancelExpiredOrderAsync(
        Order order,
        CancellationToken cancellationToken)
    {
        const string reason = "Order automatically cancelled — payment not received within 24 hours.";

        // --- Apply cancellation ---
        order.Status = OrderStatus.Cancelled;

        // --- Restore stock for each item ---
        // IN: Same stock restoration pattern as CancelOrderCommandHandler.
        // Stock decremented by PlaceOrderCommandHandler must be restored
        // when the order is cancelled — regardless of who/what cancelled it.
        // Items.Product is loaded by GetExpiredPendingOrdersAsync
        // via .Include(o => o.Items).ThenInclude(i => i.Product).
        // Without ThenInclude, item.Product would be null here.
        foreach (var item in order.Items)
        {
            if (item.Product is not null)
            {
                item.Product.StockQuantity += item.Quantity;
            }
        }

        // --- Create OutboxMessage for the cancellation event ---
        // IN: We raise an OrderCancelledEvent via the Outbox even for
        // system-initiated cancellations. The consumer (when built) sends
        // the customer an email: "Your order was cancelled because payment
        // was not received within 24 hours."
        // This is better UX than silent cancellation with no notification.
        var cancelledEvent = new OrderCancelledEvent(
            order.Id,
            order.CustomerId,
            reason);

        var outboxMessage = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            Type = typeof(OrderCancelledEvent).FullName!,
            Payload = JsonSerializer.Serialize(cancelledEvent),
            CreatedAt = DateTime.UtcNow
        };

        await _outboxRepository.AddAsync(outboxMessage, cancellationToken);

        // --- Commit atomically ---
        // IN: ONE SaveChangesAsync commits per order:
        // - UPDATE Orders SET Status = Cancelled WHERE Id = @id
        // - UPDATE Products SET StockQuantity = @restored WHERE Id = @id × N items
        // - INSERT INTO OutboxMessages (...)
        //
        // IN: Why SaveChangesAsync per order rather than once for all orders?
        // Per-order commit means a failure on order 3 does not roll back
        // the successful cancellations of orders 1 and 2.
        // One big transaction for all orders: one DB failure rolls back
        // ALL cancellations — they all retry next hour.
        // Per-order commit: partial progress is preserved.
        // The trade-off is N DB transactions vs 1. For an hourly job
        // processing at most tens of orders, N transactions is correct.
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
