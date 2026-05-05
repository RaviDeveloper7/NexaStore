// CancelOrderCommand.cs — cancels an existing order.
// INTERVIEW: Both Customer and Admin can cancel, but with different rules:
// - Customer: can only cancel their OWN orders, only in Pending/Confirmed status
// - Admin: can cancel ANY order, in any non-terminal status
// The handler enforces both rules using ICurrentUserService.
// The command itself is identical for both — the CALLER's role determines behaviour.
// This is role-based business logic, not role-based routing.

using MediatR;

namespace NexaStore.Application.Features.Orders.Commands.CancelOrder;

public class CancelOrderCommand : IRequest<Unit>
{
    public Guid OrderId { get; set; }

    // INTERVIEW: Reason is optional but valuable for audit trails.
    // "Customer requested", "Payment failed", "Expired after 24 hours" (set by OrderExpiryFunction)
    // Stored on the OrderCancelledEvent → OutboxMessage → Service Bus → email consumer.
    // The customer's cancellation email shows the reason. Good UX, good audit log.
    public string? Reason { get; set; }

    public CancelOrderCommand(Guid orderId, string? reason = null)
    {
        OrderId = orderId;
        Reason = reason;
    }
}
