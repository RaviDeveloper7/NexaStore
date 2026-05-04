using MediatR;

namespace NexaStore.Application.Features.Orders.Commands.PlaceOrder;

public class PlaceOrderCommand : IRequest<Guid>
{
    // IN: CustomerId comes from ICurrentUserService in the handler —
    // NOT from the request body. The client should never be able to place an
    // order on behalf of another user by supplying a different CustomerId.
    // The handler extracts CustomerId from the validated JWT claims.
    // This is a critical security decision — input from the client is untrusted.
    // Claims from a validated JWT are trusted.

    // The line items the customer wants to order
    public List<OrderItemRequest> Items { get; set; } = new();
}

// Nested request DTO — represents one line item in the order request.
// IN: A separate nested class (not a tuple or anonymous type) because:
// 1. FluentValidation can apply rules to it with RuleForEach()
// 2. It is self-documenting — ProductId + Quantity is the clear contract
// 3. It can evolve independently (e.g. add Notes, GiftWrapping) without
//    changing the parent command signature
public class OrderItemRequest
{
    public Guid ProductId { get; set; }
    public int Quantity { get; set; }
}
