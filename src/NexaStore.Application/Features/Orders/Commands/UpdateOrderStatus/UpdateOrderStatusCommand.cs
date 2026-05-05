// UpdateOrderStatusCommand.cs — Admin-only command to advance an order's status.
// IN: This command is Admin-only — enforced at the controller via
// [Authorize(Roles = Roles.Admin)] AND validated in the handler.
// Defence in depth: never rely on a single enforcement point.
// If the [Authorize] attribute is accidentally removed, the handler still
// validates the caller is Admin before making any change.

using MediatR;
using NexaStore.Domain.Enums;

namespace NexaStore.Application.Features.Orders.Commands.UpdateOrderStatus;

public class UpdateOrderStatusCommand : IRequest<Unit>
{
    public Guid OrderId { get; set; }

    // The target status the Admin wants to move the order to
    public OrderStatus NewStatus { get; set; }

    public UpdateOrderStatusCommand(Guid orderId, OrderStatus newStatus)
    {
        OrderId = orderId;
        NewStatus = newStatus;
    }
}
