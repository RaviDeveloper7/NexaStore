// OrderRepository.cs — order-specific query implementations.
// INTERVIEW: Orders are the most complex query target in the system.
// Key concerns here:
// 1. Role-aware paging — Customer sees own orders, Admin sees all
// 2. Explicit Include for Items + Products — prevents N+1 on order detail
// 3. Expired order query for the Azure Function timer job
// 4. Tracking vs no-tracking decisions per query type

using Microsoft.EntityFrameworkCore;
using NexaStore.Application.Common.Interfaces.Persistence;
using NexaStore.Application.Common.Models;
using NexaStore.Domain.Entities;
using NexaStore.Domain.Enums;
using NexaStore.Persistence.DatabaseContext;

namespace NexaStore.Persistence.Repositories;

public class OrderRepository : GenericRepository<Order>, IOrderRepository
{
    public OrderRepository(AppDbContext context) : base(context)
    {
    }

    public async Task<PagedResult<Order>> GetPagedAsync(
        int pageNumber,
        int pageSize,
        Guid? customerId,
        OrderStatus? status,
        CancellationToken cancellationToken = default)
    {
        var query = _dbSet
            .AsNoTracking()
            // INTERVIEW: Include Items → ThenInclude Product in one call.
            // Without these, accessing order.Items or item.Product would fire
            // separate SELECT queries per order (classic N+1 problem).
            // With them, EF generates a single JOIN query.
            .Include(o => o.Items)
                .ThenInclude(i => i.Product)
            .AsQueryable();

        // --- Role-aware filtering ---
        // INTERVIEW: customerId == null means the caller is an Admin — no filter applied,
        // all orders are returned. customerId has a value means Customer mode —
        // only their orders are returned. This single method handles both roles.
        // The handler passes ICurrentUserService.UserId for customers,
        // and null for admins. Clean, no if/else in the handler itself.
        if (customerId.HasValue)
        {
            query = query.Where(o => o.CustomerId == customerId.Value);
        }

        // --- Optional status filter ---
        if (status.HasValue)
        {
            query = query.Where(o => o.Status == status.Value);
        }

        // --- Count before pagination (same pattern as ProductRepository) ---
        var totalCount = await query.CountAsync(cancellationToken);

        // --- Default sort: most recent orders first ---
        // INTERVIEW: For order lists, newest-first is universally expected.
        // No dynamic sort here — order listing has one obvious sort.
        var items = await query
            .OrderByDescending(o => o.CreatedAt)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<Order>(items, totalCount, pageNumber, pageSize);
    }

    public async Task<Order?> GetByIdWithItemsAsync(
        Guid orderId,
        CancellationToken cancellationToken = default)
    {
        // INTERVIEW: This method is used by GetOrderByIdQueryHandler and
        // CancelOrderCommandHandler — both need the full aggregate with items.
        //
        // Key difference from GetByIdAsync (base class):
        // - GetByIdAsync uses FindAsync — only fetches the Order row, no includes.
        // - GetByIdWithItemsAsync uses FirstOrDefaultAsync with explicit Includes.
        //
        // NO AsNoTracking here — this method is also called by command handlers
        // (CancelOrder, UpdateOrderStatus) that need to modify the entity.
        // Tracked entities allow EF to detect changes and generate the correct UPDATE.
        // INTERVIEW: This is a deliberate decision — tracked for commands,
        // AsNoTracking for queries. Same repo method, different tracking behaviour
        // based on whether the caller will mutate the result.
        return await _dbSet
            .Include(o => o.Items)
                .ThenInclude(i => i.Product)
            .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken);
    }

    public async Task<IReadOnlyList<Order>> GetExpiredPendingOrdersAsync(
        DateTime cutoffTime,
        CancellationToken cancellationToken = default)
    {
        // INTERVIEW: This is called by OrderExpiryFunction — TimerTrigger every 1hr.
        // The function finds all orders still Pending after the cutoff (24hrs ago)
        // and cancels them automatically.
        //
        // No AsNoTracking here either — the function will update Status on these
        // orders and call SaveChangesAsync. Tracked entities are required for updates.
        //
        // INTERVIEW: This query hits the composite index:
        // IX_Orders_CustomerId_Status and IX_Orders_CreatedAt defined in OrderConfiguration.
        // Without the index, every timer execution does a full table scan on Orders.
        return await _dbSet
            .Where(o =>
                o.Status == OrderStatus.Pending &&
                o.CreatedAt < cutoffTime)
            .ToListAsync(cancellationToken);
    }
}
