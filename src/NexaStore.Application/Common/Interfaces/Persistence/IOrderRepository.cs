// IOrderRepository.cs — order-specific query contracts.
// INTERVIEW: Orders need special handling — a Customer can only see their own
// orders, but an Admin can see all. This filtering logic is expressed here
// as a specific method contract rather than hacking it into the generic repo.

using NexaStore.Application.Common.Models;
using NexaStore.Domain.Entities;
using NexaStore.Domain.Enums;

namespace NexaStore.Application.Common.Interfaces.Persistence;

public interface IOrderRepository : IGenericRepository<Order>
{
    // Paged order list with optional customer filter
    // customerId = null means Admin mode — returns all orders
    // customerId = Guid means Customer mode — returns only their orders
    // INTERVIEW: Role-based data filtering at the repository level keeps
    // the handler clean — handler just passes the ID, repo handles the WHERE clause
    Task<PagedResult<Order>> GetPagedAsync(
        int pageNumber,
        int pageSize,
        Guid? customerId,           // null = Admin (all orders), Guid = Customer (own only)
        OrderStatus? status,        // Optional filter by status
        CancellationToken cancellationToken = default);

    // Loads an Order with its Items and Products in a single query
    // INTERVIEW: Explicit include methods prevent N+1 query problems.
    // Without this, accessing order.Items would fire a separate SQL query per order.
    Task<Order?> GetByIdWithItemsAsync(
        Guid orderId,
        CancellationToken cancellationToken = default);

    // Used by OrderExpiryFunction — finds all Pending orders older than the cutoff
    // INTERVIEW: This is a perfect example of why domain-specific repos exist.
    // You can't express "Pending orders older than X hours" cleanly in a generic repo.
    Task<IReadOnlyList<Order>> GetExpiredPendingOrdersAsync(
        DateTime cutoffTime,
        CancellationToken cancellationToken = default);
}
