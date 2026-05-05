// GetOrdersQuery.cs — paged order list, role-aware.
// IN: One query handles both Customer (own orders) and Admin (all orders).
// The handler uses ICurrentUserService to determine which data to return.
// The controller sends the same query regardless of role — clean, no branching
// at the HTTP layer. Role-based data filtering is a handler responsibility.
//
// IN: Why not two separate queries — GetMyOrdersQuery and GetAllOrdersQuery?
// They share the exact same shape, parameters, and return type.
// The ONLY difference is the WHERE clause — filtered by CustomerId or not.
// One query + one handler with a conditional is simpler and safer than
// maintaining two parallel implementations that must stay in sync.

using MediatR;
using NexaStore.Application.Common.Models;
using NexaStore.Application.Features.Orders.Queries.GetOrders;
using NexaStore.Domain.Enums;

namespace NexaStore.Application.Features.Orders.Queries.GetOrders;

public class GetOrdersQuery : IRequest<PagedResult<OrderListDto>>
{
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 10;

    // Optional status filter — Admin can view "all Pending orders" for fulfilment dashboard
    // Customer can view "my Cancelled orders" for order history
    public OrderStatus? Status { get; set; }

    // INTERVIEW: GetOrdersQuery does NOT extend PaginationParams (unlike GetProductsQuery).
    // Orders have fewer filter options — no search term, no sort direction.
    // Using PaginationParams would bring in SearchTerm, SortBy, IsDescending —
    // fields irrelevant to orders. Explicit properties keep the contract tight.
}
