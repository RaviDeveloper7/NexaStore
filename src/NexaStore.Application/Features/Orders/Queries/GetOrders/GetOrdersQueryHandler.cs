// GetOrdersQueryHandler.cs — role-aware paged order list.
// IN: This handler demonstrates the "same handler, different data" pattern.
// The role-based filter is applied at the repository level via a nullable CustomerId:
//   CustomerId = null   → Admin path  → no WHERE clause on CustomerId
//   CustomerId = Guid   → Customer path → WHERE CustomerId = @id
//
// IN: Why not cache order lists like we cache product lists?
// Product catalog: read-heavy, write-infrequent. Orders change state constantly
// (Pending → Confirmed → Shipped → Delivered). A 5-minute stale order list
// is unacceptable — the customer needs to see their current order status.
// Caching order lists would require aggressive invalidation on every status change,
// every new order, every cancellation — effectively no cache benefit.
// Rule: cache reference data (products, categories). Don't cache transactional data (orders).

using MapsterMapper;
using MediatR;
using NexaStore.Application.Common.Interfaces.Identity;
using NexaStore.Application.Common.Interfaces.Persistence;
using NexaStore.Application.Common.Models;
using NexaStore.Domain.Exceptions;

namespace NexaStore.Application.Features.Orders.Queries.GetOrders;

public class GetOrdersQueryHandler
    : IRequestHandler<GetOrdersQuery, PagedResult<OrderListDto>>
{
    private readonly IOrderRepository _orderRepository;
    private readonly ICurrentUserService _currentUserService;
    private readonly IMapper _mapper;

    public GetOrdersQueryHandler(
        IOrderRepository orderRepository,
        ICurrentUserService currentUserService,
        IMapper mapper)
    {
        _orderRepository = orderRepository;
        _currentUserService = currentUserService;
        _mapper = mapper;
    }

    public async Task<PagedResult<OrderListDto>> Handle(
        GetOrdersQuery query,
        CancellationToken cancellationToken)
    {
        // =====================================================================
        // STEP 1: Determine the caller's role and resolve CustomerId filter
        // =====================================================================

        Guid? customerId = null; // null = Admin mode — no customer filter

        if (!_currentUserService.IsAdmin)
        {
            // Customer mode — filter to only their own orders
            // IN: Customers must never see other customers' orders.
            // This is enforced HERE in the handler, not just in the repository.
            // The repository receives the filtered CustomerId and applies it.
            // Belt-and-suspenders: even if the repo filter fails, this handler
            // would need to be rewritten to break customer isolation.
            var userId = _currentUserService.UserId
                ?? throw new UnauthorizedAccessException(
                    "You must be authenticated to view orders.");

            if (!Guid.TryParse(userId, out var callerGuid))
                throw new UnauthorizedAccessException(
                    "Invalid user identity. Please log in again.");

            customerId = callerGuid;
        }

        // =====================================================================
        // STEP 2: Query the repository with role-aware filter
        // =====================================================================

        // IN: GetPagedAsync signature:
        //   customerId = null  → returns ALL orders (Admin)
        //   customerId = Guid  → returns only that customer's orders (Customer)
        // Repository enforces the SQL-level filter — Application layer controls
        // WHAT to filter, Repository controls HOW it's filtered in SQL.
        var pagedOrders = await _orderRepository.GetPagedAsync(
            pageNumber: query.PageNumber,
            pageSize: query.PageSize,
            customerId: customerId,
            status: query.Status,
            cancellationToken: cancellationToken);

        // =====================================================================
        // STEP 3: Map entities to DTOs
        // =====================================================================

        // IN: OrderListDto.ItemCount is mapped from Items.Count.
        // GetPagedAsync Includes Items — so Items is populated and countable.
        // Mapster's OrderMappingProfile maps Items.Count → ItemCount automatically.
        // The Items collection itself is NOT included in OrderListDto —
        // only the count. This keeps the list payload lean.
        var dtos = _mapper.Map<List<OrderListDto>>(pagedOrders.Items);

        return new PagedResult<OrderListDto>(
            dtos,
            pagedOrders.TotalCount,
            pagedOrders.PageNumber,
            pagedOrders.PageSize);
    }
}
