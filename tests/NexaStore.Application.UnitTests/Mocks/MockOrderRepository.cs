// MockOrderRepository.cs — in-memory fake of IOrderRepository.
// Same rationale as MockProductRepository — hand-written fake over Moq
// because the interface has enough query complexity to warrant real filtering logic.

using System.Linq.Expressions;
using NexaStore.Application.Common.Interfaces.Persistence;
using NexaStore.Application.Common.Models;
using NexaStore.Domain.Entities;
using NexaStore.Domain.Enums;

namespace NexaStore.Application.UnitTests.Mocks;

public class MockOrderRepository : IOrderRepository
{
    public List<Order> Orders { get; } = new();

    public Task<IReadOnlyList<Order>> GetAllAsync(
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<Order>>(Orders.ToList());

    public Task<Order?> GetByIdAsync(
        Guid id, CancellationToken cancellationToken = default)
        => Task.FromResult(Orders.FirstOrDefault(o => o.Id == id));

    public Task<IReadOnlyList<Order>> GetAsync(
        Expression<Func<Order, bool>> predicate,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<Order>>(
            Orders.AsQueryable().Where(predicate).ToList());

    public Task<bool> ExistsAsync(
        Expression<Func<Order, bool>> predicate,
        CancellationToken cancellationToken = default)
        => Task.FromResult(Orders.AsQueryable().Any(predicate));

    public Task AddAsync(
        Order entity, CancellationToken cancellationToken = default)
    {
        Orders.Add(entity);
        return Task.CompletedTask;
    }

    public void Update(Order entity) { /* no-op — same reference in-memory */ }

    public void Delete(Order entity) => Orders.Remove(entity);

    public Task<PagedResult<Order>> GetPagedAsync(
        int pageNumber, int pageSize, Guid? customerId, OrderStatus? status,
        CancellationToken cancellationToken = default)
    {
        var query = Orders.AsQueryable();

        if (customerId.HasValue)
            query = query.Where(o => o.CustomerId == customerId.Value);

        if (status.HasValue)
            query = query.Where(o => o.Status == status.Value);

        var totalCount = query.Count();

        var items = query
            .OrderByDescending(o => o.CreatedAt)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return Task.FromResult(
            new PagedResult<Order>(items, totalCount, pageNumber, pageSize));
    }

    public Task<Order?> GetByIdWithItemsAsync(
        Guid orderId, CancellationToken cancellationToken = default)
        => Task.FromResult(Orders.FirstOrDefault(o => o.Id == orderId));

    public Task<IReadOnlyList<Order>> GetExpiredPendingOrdersAsync(
        DateTime cutoffTime, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<Order>>(
            Orders.Where(o =>
                o.Status == OrderStatus.Pending &&
                o.CreatedAt < cutoffTime).ToList());
}
