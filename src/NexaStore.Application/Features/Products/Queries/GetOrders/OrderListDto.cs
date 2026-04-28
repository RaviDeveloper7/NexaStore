// OrderListDto.cs — summary order representation for list/dashboard views.
// INTERVIEW: Notice there is no Items collection here.
// Loading items for every order in a paginated list is an N+1 waiting to happen.
// The list view only needs the order summary — status, total, item count.
// Full items are loaded by GetOrderByIdQueryHandler via GetByIdWithItemsAsync.

using NexaStore.Domain.Enums;

namespace NexaStore.Application.Features.Orders.Queries.GetOrders;

public class OrderListDto
{
    public Guid Id { get; set; }
    public Guid CustomerId { get; set; }
    public OrderStatus Status { get; set; }

    // Human-readable status — maps enum to string for API consumers
    // INTERVIEW: Exposing the enum integer to the client is fragile —
    // if enum values change, client code breaks silently.
    // Exposing the string name is self-documenting and version-safe.
    public string StatusName => Status.ToString();

    public decimal TotalAmount { get; set; }
    public int ItemCount { get; set; }  // Mapped from Items.Count
    public DateTime CreatedAt { get; set; }
}
