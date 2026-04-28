// OrderDetailDto.cs — full order representation including all line items.

using NexaStore.Domain.Enums;

namespace NexaStore.Application.Features.Orders.Queries.GetOrderById;

public class OrderDetailDto
{
    public Guid Id { get; set; }
    public Guid CustomerId { get; set; }
    public OrderStatus Status { get; set; }
    public string StatusName => Status.ToString();
    public decimal TotalAmount { get; set; }

    // Full line items — only loaded on detail view
    public List<OrderItemDto> Items { get; set; } = new();

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

// Nested DTO — lives here because it only makes sense in the context of OrderDetailDto
// INTERVIEW: Nested DTOs avoid polluting the namespace with a class that has
// no independent meaning. OrderItemDto without an order context is meaningless.
public class OrderItemDto
{
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }

    // Computed line total — avoids the client having to multiply
    public decimal LineTotal => Quantity * UnitPrice;
}
