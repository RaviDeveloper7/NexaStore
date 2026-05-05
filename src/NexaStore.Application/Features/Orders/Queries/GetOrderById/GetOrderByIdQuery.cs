// GetOrderByIdQuery.cs — fetches a single order with full line items.
// IN: Returns OrderDetailDto — includes the full Items collection.
// GetOrdersQuery (list) returns OrderListDto with only ItemCount.
// GetOrderById (detail) returns OrderDetailDto with full Items + ProductName per item.
// Two DTOs, two levels of detail — standard REST resource/collection pattern.

using MediatR;
using NexaStore.Application.Features.Orders.Queries.GetOrderById;

namespace NexaStore.Application.Features.Orders.Queries.GetOrderById;

public class GetOrderByIdQuery : IRequest<OrderDetailDto>
{
    public Guid OrderId { get; set; }

    public GetOrderByIdQuery(Guid orderId) => OrderId = orderId;
}
