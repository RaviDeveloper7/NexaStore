// OrderMappingProfile.cs — Mapster configuration for Order and OrderItem entities.
// INTERVIEW: This profile demonstrates two important Mapster features:
// 1. Mapping a COUNT from a collection (Items.Count → ItemCount)
// 2. Chained mapping — when Order maps to OrderDetailDto, the Items collection
//    is automatically mapped to List<OrderItemDto> because we also define
//    the OrderItem → OrderItemDto mapping in the same config.

using Mapster;
using NexaStore.Application.Features.Orders.Queries.GetOrderById;
using NexaStore.Application.Features.Orders.Queries.GetOrders;
using NexaStore.Domain.Entities;

namespace NexaStore.Application.Common.Mappings;

public class OrderMappingProfile : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        // --- OrderItem → OrderItemDto ---
        // Register this FIRST — Order mapping below depends on it for Items collection
        config.NewConfig<OrderItem, OrderItemDto>()

            // ProductName lives on OrderItem.Product.Name — not directly on OrderItem
            // INTERVIEW: This is why we .Include(o => o.Items).ThenInclude(i => i.Product)
            // in the repository. If Product is null here, the map would throw.
            // The repository guarantees it's loaded — that's the contract.
            .Map(dest => dest.ProductName,
                 src => src.Product != null ? src.Product.Name : string.Empty)

            // Id, ProductId, Quantity, UnitPrice — auto-mapped by name
            // LineTotal — computed property on DTO, no setter, not mapped
            .IgnoreNonMapped(false);

        // --- Order → OrderListDto ---
        config.NewConfig<Order, OrderListDto>()

            // INTERVIEW: Items.Count → ItemCount.
            // Mapster can map a collection's count to an integer property.
            // This avoids loading Items on the list query — but note: for this
            // to work, Items must be loaded (or at least countable) in the query.
            // GetOrdersQueryHandler uses GetPagedAsync which Includes Items — correct.
            .Map(dest => dest.ItemCount,
                 src => src.Items != null ? src.Items.Count : 0)

            // CustomerId, Status, TotalAmount, CreatedAt — auto-mapped
            // StatusName — computed on DTO, no setter
            .IgnoreNonMapped(false);

        // --- Order → OrderDetailDto ---
        config.NewConfig<Order, OrderDetailDto>()

            // Items collection — Mapster automatically maps ICollection<OrderItem>
            // to List<OrderItemDto> using the OrderItem → OrderItemDto config above.
            // INTERVIEW: This is "chained mapping" — no explicit configuration needed
            // for the Items collection because the element mapping is already registered.
            // Mapster discovers it automatically at startup when Register() runs.

            // CustomerId, Status, TotalAmount, CreatedAt, UpdatedAt — auto-mapped
            .IgnoreNonMapped(false);
    }
}
