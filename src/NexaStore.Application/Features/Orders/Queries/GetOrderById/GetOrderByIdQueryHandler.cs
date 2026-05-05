// GetOrderByIdQueryHandler.cs — fetches full order detail with ownership enforcement.
// IN: Like GetOrders, this handler enforces resource ownership.
// A Customer requesting GET /orders/{id} for an order that belongs to another
// customer gets a 404 — not a 403. Why?
//
// IN: 403 vs 404 for resource ownership violations:
// 403 Forbidden: "I know this resource exists, you can't access it."
//   → confirms the resource exists → information leakage
//   → attacker learns order IDs exist by probing for 403 vs 404
// 404 Not Found: "This resource doesn't exist for you."
//   → from the caller's perspective, it genuinely doesn't exist
//   → no information leakage about other users' orders
//
// This is called "security through ambiguity" — the correct approach for
// multi-tenant APIs where resource existence should not be disclosed
// to unauthorized parties.

using MapsterMapper;
using MediatR;
using NexaStore.Application.Common.Interfaces.Identity;
using NexaStore.Application.Common.Interfaces.Persistence;
using NexaStore.Domain.Entities;
using NexaStore.Domain.Exceptions;

namespace NexaStore.Application.Features.Orders.Queries.GetOrderById;

public class GetOrderByIdQueryHandler
    : IRequestHandler<GetOrderByIdQuery, OrderDetailDto>
{
    private readonly IOrderRepository _orderRepository;
    private readonly ICurrentUserService _currentUserService;
    private readonly IMapper _mapper;

    public GetOrderByIdQueryHandler(
        IOrderRepository orderRepository,
        ICurrentUserService currentUserService,
        IMapper mapper)
    {
        _orderRepository = orderRepository;
        _currentUserService = currentUserService;
        _mapper = mapper;
    }

    public async Task<OrderDetailDto> Handle(
        GetOrderByIdQuery query,
        CancellationToken cancellationToken)
    {
        // =====================================================================
        // STEP 1: Load the order with full Items + Products
        // =====================================================================

        // IN: GetByIdWithItemsAsync uses:
        // .Include(o => o.Items).ThenInclude(i => i.Product)
        // This loads in a single JOIN query:
        //   Orders + OrderItems + Products
        // Without ThenInclude, item.Product would be null — Mapster would
        // map ProductName as string.Empty from the OrderMappingProfile.
        // The Include chain guarantees full data for the detail view.
        //
        // IN: No AsNoTracking here — see OrderRepository.
        // GetByIdWithItemsAsync is also used by CancelOrderCommandHandler
        // which needs tracking. For query-only handlers like this one, the
        // lack of AsNoTracking is a minor performance trade-off (EF tracks
        // the entity but we never modify it). Acceptable for consistency.
        // Alternative: add a separate GetByIdWithItemsAsNoTrackingAsync —
        // explicit is always better for high-traffic production systems.
        var order = await _orderRepository
            .GetByIdWithItemsAsync(query.OrderId, cancellationToken)
            ?? throw new NotFoundException(nameof(Order), query.OrderId);

        // =====================================================================
        // STEP 2: Ownership enforcement — return 404 for unauthorized access
        // =====================================================================

        if (!_currentUserService.IsAdmin)
        {
            var userId = _currentUserService.UserId;

            if (!Guid.TryParse(userId, out var callerGuid) ||
                order.CustomerId != callerGuid)
                // INTERVIEW: 404 not 403 — discussed in the class comment above.
                // We throw NotFoundException (not UnauthorizedAccessException)
                // to avoid disclosing that the order exists to the wrong caller.
                throw new NotFoundException(nameof(Order), query.OrderId);
        }

        // =====================================================================
        // STEP 3: Map and return
        // =====================================================================

        // IN: Mapster maps Order → OrderDetailDto including:
        // Items (ICollection<OrderItem>) → Items (List<OrderItemDto>)
        // Each OrderItem → OrderItemDto with ProductName from item.Product.Name
        // This chained mapping works because OrderMappingProfile registers
        // both Order → OrderDetailDto and OrderItem → OrderItemDto.
        // Mapster discovers the element mapping automatically.
        return _mapper.Map<OrderDetailDto>(order);
    }
}
