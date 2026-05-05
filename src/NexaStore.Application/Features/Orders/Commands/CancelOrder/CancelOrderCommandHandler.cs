// CancelOrderCommandHandler.cs — enforces cancellation rules and raises the domain event.
// IN: Three state machine rules enforced here:
// 1. Order must exist                          → NotFoundException (404)
// 2. Caller must own the order (if Customer)   → UnauthorizedAccessException (403 via middleware)
// 3. Order must be in a cancellable status     → BadRequestException (400)
//
// IN: Why enforce ownership in the handler and not in a policy/filter?
// Authorization filters check "can this role access this endpoint?"
// Business rule checks check "can this specific user act on this specific resource?"
// These are different concerns. [Authorize(Roles = "Customer")] can't know
// if Order 123 belongs to the calling user — only the handler can check that
// after loading the Order from the DB. Always enforce resource ownership in handlers.

using System.Text.Json;
using MediatR;
using NexaStore.Application.Common.Interfaces.Identity;
using NexaStore.Application.Common.Interfaces.Persistence;
using NexaStore.Application.Common.Interfaces.Services;
using NexaStore.Domain.Entities;
using NexaStore.Domain.Enums;
using NexaStore.Domain.Events;
using NexaStore.Domain.Exceptions;

namespace NexaStore.Application.Features.Orders.Commands.CancelOrder;

public class CancelOrderCommandHandler
    : IRequestHandler<CancelOrderCommand, Unit>
{
    private readonly IOrderRepository _orderRepository;
    private readonly IOutboxRepository _outboxRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserService _currentUserService;

    public CancelOrderCommandHandler(
        IOrderRepository orderRepository,
        IOutboxRepository outboxRepository,
        IUnitOfWork unitOfWork,
        ICurrentUserService currentUserService)
    {
        _orderRepository = orderRepository;
        _outboxRepository = outboxRepository;
        _unitOfWork = unitOfWork;
        _currentUserService = currentUserService;
    }

    public async Task<Unit> Handle(
        CancelOrderCommand command,
        CancellationToken cancellationToken)
    {
        // =====================================================================
        // STEP 1: Load the order with its items
        // =====================================================================

        // IN: GetByIdWithItemsAsync — tracked (no AsNoTracking).
        // We need Items to restore stock, and we need tracking so
        // EF detects the Status change and generates the correct UPDATE.
        var order = await _orderRepository
            .GetByIdWithItemsAsync(command.OrderId, cancellationToken)
            ?? throw new NotFoundException(nameof(Order), command.OrderId);

        // =====================================================================
        // STEP 2: Enforce ownership (Customer only)
        // =====================================================================

        // IN: Admins bypass the ownership check — they can cancel any order.
        // Customers may only cancel their own orders.
        // This conditional ownership pattern is common in enterprise APIs.
        // The alternative — two separate endpoints (Customer vs Admin) — leads to
        // duplicated handler logic. One handler with a role check is cleaner.
        if (!_currentUserService.IsAdmin)
        {
            var callerId = _currentUserService.UserId;

            if (!Guid.TryParse(callerId, out var callerGuid) ||
                order.CustomerId != callerGuid)
                // IN: We throw UnauthorizedAccessException (not NotFoundException)
                // because the resource EXISTS — we're not hiding it.
                // Throwing NotFoundException for an ownership mismatch would be
                // "security through obscurity" — revealing a different order ID exists.
                // Since we're in an authenticated context and the user is logged in,
                // it is safe to say "you don't have permission" rather than "not found".
                throw new UnauthorizedAccessException(
                    "You do not have permission to cancel this order.");
        }

        // =====================================================================
        // STEP 3: Enforce the state machine
        // =====================================================================

        // IN: This is the Order state machine:
        // Pending → Confirmed → Shipped → Delivered  (terminal success)
        //         ↘ Cancelled                        (terminal failure)
        //                   ↗ (Confirmed can also be cancelled)
        //
        // Cancellable states: Pending, Confirmed
        // Non-cancellable: Shipped, Delivered (goods in transit or received)
        // Already cancelled: idempotent — no error, no-op
        //
        // IN: Why return success for Already Cancelled?
        // Idempotency — if the client retries a cancel request (network timeout,
        // duplicate click), they should get success, not an error.
        // The end state is what they wanted — Cancelled. Mission accomplished.
        if (order.Status == OrderStatus.Cancelled)
            // Already in the desired state — idempotent success
            return Unit.Value;

        // IN: HashSet<T>.Contains is O(1) — faster than List<T>.Contains which is O(n).
        // For a small set this is micro-optimisation, but using HashSet signals
        // you understand the data structure's complexity characteristics.
        var cancellableStatuses = new HashSet<OrderStatus>
        {
            OrderStatus.Pending,
            OrderStatus.Confirmed
        };

        if (!cancellableStatuses.Contains(order.Status))
            throw new BadRequestException(
                $"Order cannot be cancelled. Current status: {order.Status}. " +
                $"Only Pending and Confirmed orders can be cancelled.");

        // =====================================================================
        // STEP 4: Apply state change and restore stock
        // =====================================================================

        // Transition to Cancelled
        order.Status = OrderStatus.Cancelled;

        // IN: Restore stock when an order is cancelled.
        // Every item that was decremented on PlaceOrder must be incremented back.
        // This is applied in-memory on tracked entities — EF generates
        // UPDATE Products SET StockQuantity = @restored WHERE Id = @id
        // for each product, in the same transaction as the Order status update.
        // No stock is restored for Delivered orders (already non-cancellable)
        // or Shipped orders (goods already dispatched — stock is physically gone).
        foreach (var item in order.Items)
        {
            // We can't increment through _productRepository here without
            // loading each product. Instead update via IGenericRepository<Product>
            // injected through the EF context — the tracked item.Product navigation
            // was loaded by GetByIdWithItemsAsync (ThenInclude(i => i.Product)).
            // IN: item.Product is loaded and tracked — property change
            // is detected by EF change tracker automatically.
            if (item.Product is not null)
            {
                item.Product.StockQuantity += item.Quantity;
            }
        }

        // =====================================================================
        // STEP 5: Raise domain event and create OutboxMessage
        // =====================================================================

        var reason = command.Reason ?? "Customer requested cancellation";

        order.AddDomainEvent(new OrderCancelledEvent(
            order.Id,
            order.CustomerId,
            reason));

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

        // =====================================================================
        // STEP 6: Commit atomically
        // =====================================================================

        // IN: ONE SaveChangesAsync commits:
        // - UPDATE Orders SET Status = Cancelled WHERE Id = @id
        // - UPDATE Products SET StockQuantity = @restored WHERE Id = @id × N items
        // - INSERT INTO OutboxMessages (...)
        // All three succeed or all three roll back together.
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
