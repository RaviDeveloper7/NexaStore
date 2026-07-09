using System.Text.Json;
using MediatR;
using NexaStore.Application.Common.Interfaces.Identity;
using NexaStore.Application.Common.Interfaces.Persistence;
using NexaStore.Application.Common.Interfaces.Services;
using NexaStore.Domain.Entities;
using NexaStore.Domain.Enums;
using NexaStore.Domain.Events;
using NexaStore.Domain.Exceptions;

namespace NexaStore.Application.Features.Orders.Commands.PlaceOrder;

public class PlaceOrderCommandHandler : IRequestHandler<PlaceOrderCommand, Guid>
{
    private readonly IOrderRepository _orderRepository;
    private readonly IProductRepository _productRepository;
    private readonly IOutboxRepository _outboxRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserService _currentUserService;

    public PlaceOrderCommandHandler(
        IOrderRepository orderRepository,
        IProductRepository productRepository,
        IOutboxRepository outboxRepository,
        IUnitOfWork unitOfWork,
        ICurrentUserService currentUserService)
    {
        _orderRepository = orderRepository;
        _productRepository = productRepository;
        _outboxRepository = outboxRepository;
        _unitOfWork = unitOfWork;
        _currentUserService = currentUserService;
    }

    public async Task<Guid> Handle(PlaceOrderCommand command, CancellationToken cancellationToken)
    {
        // IN: CustomerId comes from the JWT claim, NOT from the request body.
        // Any authenticated user could otherwise place orders on behalf of others.
        // ICurrentUserService reads the "sub" claim from the validated JWT.
        var customerId = _currentUserService.UserId
            ?? throw new UnauthorizedAccessException(
                "You must be authenticated to place an order.");

        // IN: ASP.NET Core Identity uses string IDs; our domain uses Guid for CustomerId.
        // Parse at the boundary to keep the domain clean.
        if (!Guid.TryParse(customerId, out var customerGuid))
            throw new UnauthorizedAccessException(
                "Invalid user identity. Please log in again.");

        // Batch-fetch all requested products in ONE DB query
        var productIds = command.Items.Select(i => i.ProductId).ToList();

        // IN: Batch fetch prevents N+1 queries — one SELECT instead of per-item queries.
        var products = await _productRepository
            .GetByIdsTrackedAsync(productIds, cancellationToken);

        // Validate all products exist
        // IN: Dictionary lookup is O(1) vs nested loop O(n²) with FirstOrDefault.
        var productDict = products.ToDictionary(p => p.Id);

        foreach (var item in command.Items)
        {
            if (!productDict.ContainsKey(item.ProductId))
                // IN: Entire order fails if any product doesn't exist (all-or-nothing).
                throw new NotFoundException(nameof(Product), item.ProductId);
        }

        // Validate stock before any decrements
        // IN: Validate all before decrementing prevents partial state.
        foreach (var item in command.Items)
        {
            var product = productDict[item.ProductId];

            if (product.StockQuantity < item.Quantity)
                // IN: Structured exception data aids debugging and error logging.
                throw new InsufficientStockException(
                    product.Id,
                    item.Quantity,
                    product.StockQuantity);
        }

        // All stock checks passed — now decrement
        // IN: EF's change tracker detects property changes and generates UPDATEs.
        // All changes are committed in a single SaveChangesAsync transaction.
        foreach (var item in command.Items)
        {
            productDict[item.ProductId].StockQuantity -= item.Quantity;
        }

        // IN: Production enhancement: use ExecuteUpdateAsync with WHERE clause for optimistic concurrency.
        // Current approach is acceptable for portfolio scope.

        // Build the Order aggregate
        var order = new Order
        {
            Id = Guid.NewGuid(),
            CustomerId = customerGuid,
            Status = OrderStatus.Pending,
        };

        foreach (var item in command.Items)
        {
            var product = productDict[item.ProductId];

            // IN: Order is an Aggregate Root; all state changes go through AddItem().
            order.AddItem(new OrderItem
            {
                Id = Guid.NewGuid(),
                OrderId = order.Id,
                ProductId = item.ProductId,
                Quantity = item.Quantity,
                // IN: UnitPrice stores the agreed price; not a reference to Product.Price.
                UnitPrice = product.Price
            });
        }

        // IN: AddDomainEvent queues event for in-process MediatR dispatch after SaveChanges.
        // Cross-process delivery uses OutboxMessage (see Step 7).
        order.AddDomainEvent(new OrderPlacedEvent(
            order.Id,
            customerGuid,
            order.TotalAmount));

        // IN: Outbox Pattern — persist Order + OutboxMessage atomically in ONE transaction.
        // Ensures at-least-once cross-service delivery without message loss.
        await _orderRepository.AddAsync(order, cancellationToken);

        var orderPlacedEvent = new OrderPlacedEvent(
            order.Id,
            customerGuid,
            order.TotalAmount);

        // IN: Separate event instances for in-process (aggregate) vs outbox (cross-process).
        var outboxMessage = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            Type = typeof(OrderPlacedEvent).FullName!,
            // IN: System.Text.Json serialization; Type field for deserialization.
            Payload = JsonSerializer.Serialize(orderPlacedEvent),
            CreatedAt = DateTime.UtcNow
        };

        await _outboxRepository.AddAsync(outboxMessage, cancellationToken);

        // IN: SaveChangesAsync commits all changes in ONE transaction:
        // INSERT Order, INSERT OrderItems (N), UPDATE Products (N), INSERT OutboxMessage.
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return order.Id;
    }
}
