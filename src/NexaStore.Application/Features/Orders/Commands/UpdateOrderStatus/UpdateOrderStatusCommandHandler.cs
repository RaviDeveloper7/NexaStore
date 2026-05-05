// UpdateOrderStatusCommandHandler.cs — enforces the full Order state machine.
// IN: This handler is the single source of truth for valid status transitions.
// It prevents invalid state changes like:
//   Delivered → Pending   (impossible — goods already received)
//   Cancelled → Shipped   (impossible — cancelled orders cannot be revived)
//   Shipped   → Confirmed (backwards transition — not allowed)
//
// IN: Why a dictionary of valid transitions over a series of if/else?
// Dictionary<TKey, HashSet<TValue>> is a transition table — the explicit,
// readable representation of the state machine. If you need to add a new
// transition (e.g. "allow Confirmed → Cancelled"), you add one line to the
// dictionary. An if/else chain requires finding the right nested condition
// and inserting correctly — fragile and hard to reason about.
// The dictionary IS the state machine documentation.

using MediatR;
using NexaStore.Application.Common.Interfaces.Identity;
using NexaStore.Application.Common.Interfaces.Persistence;
using NexaStore.Domain.Entities;
using NexaStore.Domain.Enums;
using NexaStore.Domain.Exceptions;

namespace NexaStore.Application.Features.Orders.Commands.UpdateOrderStatus;

public class UpdateOrderStatusCommandHandler
    : IRequestHandler<UpdateOrderStatusCommand, Unit>
{
    private readonly IOrderRepository _orderRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserService _currentUserService;

    // IN: The state machine as a static readonly dictionary.
    // Static — one instance for the lifetime of the application, not rebuilt per request.
    // Readonly — immutable after initialisation, thread-safe.
    // Dictionary<OrderStatus, HashSet<OrderStatus>>:
    //   Key   = current status
    //   Value = set of valid target statuses FROM that current status
    //
    // Reading this dictionary tells you the entire state machine at a glance:
    // Pending   can go to → Confirmed, Cancelled
    // Confirmed can go to → Shipped, Cancelled
    // Shipped   can go to → Delivered (only)
    // Delivered can go to → (nothing — terminal state)
    // Cancelled can go to → (nothing — terminal state)
    private static readonly Dictionary<OrderStatus, HashSet<OrderStatus>> ValidTransitions =
        new()
        {
            // IN: Pending is the starting state. Two exits:
            // Admin confirms the order (payment verified, ready to fulfil)
            // Admin or Customer cancels (rejected or customer changed mind)
            [OrderStatus.Pending] = new()
            {
                OrderStatus.Confirmed,
                OrderStatus.Cancelled
            },

            // Confirmed = payment verified, being prepared for dispatch.
            // Can advance to Shipped or be cancelled (before dispatch only).
            [OrderStatus.Confirmed] = new()
            {
                OrderStatus.Shipped,
                OrderStatus.Cancelled
            },

            // Shipped = goods dispatched. Only one valid next state.
            // Cannot cancel a shipped order — goods are in transit.
            [OrderStatus.Shipped] = new()
            {
                OrderStatus.Delivered
            },

            // Terminal states — no further transitions allowed.
            // IN: Including terminal states in the dictionary with empty
            // sets makes the "no transition possible" case explicit — it falls
            // through the same validation logic rather than needing a special case.
            [OrderStatus.Delivered] = new(),
            [OrderStatus.Cancelled] = new()
        };

    public UpdateOrderStatusCommandHandler(
        IOrderRepository orderRepository,
        IUnitOfWork unitOfWork,
        ICurrentUserService currentUserService)
    {
        _orderRepository = orderRepository;
        _unitOfWork = unitOfWork;
        _currentUserService = currentUserService;
    }

    public async Task<Unit> Handle(
        UpdateOrderStatusCommand command,
        CancellationToken cancellationToken)
    {
        // =====================================================================
        // STEP 1: Admin-only enforcement (defence in depth)
        // =====================================================================

        // IN: Double enforcement — [Authorize(Roles = "Admin")] on the
        // controller is the first gate. This check is the second gate.
        // A misconfigured controller, a middleware bug, or a direct Function call
        // bypassing HTTP middleware could reach this handler without the attribute.
        // The handler must be self-defending.
        if (!_currentUserService.IsAdmin)
            throw new UnauthorizedAccessException(
                "Only administrators can update order status.");

        // =====================================================================
        // STEP 2: Load the order
        // =====================================================================

        // GetByIdAsync (FindAsync) is sufficient here — no items needed
        // for a status update. Avoids the JOIN from GetByIdWithItemsAsync.
        // IN: Always load only what you need. Unnecessary JOINs
        // waste DB resources on every status update call.
        var order = await _orderRepository
            .GetByIdAsync(command.OrderId, cancellationToken)
            ?? throw new NotFoundException(nameof(Order), command.OrderId);

        // =====================================================================
        // STEP 3: Idempotency check
        // =====================================================================

        // IN: If the order is already in the requested status,
        // return success immediately — do not throw an error.
        // This handles duplicate requests gracefully (network retry, webhook replay).
        if (order.Status == command.NewStatus)
            return Unit.Value;

        // =====================================================================
        // STEP 4: State machine validation
        // =====================================================================

        // Look up the valid transitions from the current status
        if (!ValidTransitions.TryGetValue(order.Status, out var validNextStatuses ))
            throw new BadRequestException(
                $"Unrecognised current order status: {order.Status}.");

        if (!validNextStatuses.Contains(command.NewStatus))
            throw new BadRequestException(
                $"Invalid status transition: {order.Status} → {command.NewStatus}. " +
                $"Valid transitions from {order.Status}: " +
                $"{(validNextStatuses.Any() ? string.Join(", ", validNextStatuses) : "none (terminal state)")}.");
        // IN: The error message explicitly lists valid transitions.
        // This is invaluable for API consumers and support teams debugging
        // "why did my status update fail?" — no guessing required.

        // =====================================================================
        // STEP 5: Apply state change and save
        // =====================================================================

        order.Status = command.NewStatus;
        // UpdatedAt is set by AppDbContext.SaveChangesAsync audit interception

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // IN: No OutboxMessage here — UpdateOrderStatus is an internal
        // Admin operation. In a real system you might raise an OrderStatusChangedEvent
        // to notify the customer via email. For NexaStore's scope, the Outbox is
        // used for OrderPlaced and OrderCancelled only.
        // Adding it here is a one-line extension: create OutboxMessage with
        // typeof(OrderStatusChangedEvent).FullName! — same pattern as PlaceOrder.

        return Unit.Value;
    }
}
