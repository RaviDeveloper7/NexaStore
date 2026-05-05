// PlaceOrderCommandHandler.cs — the most important handler in the entire solution.
// IN: Every major pattern in the codebase converges here.
// Walk through this handler line-by-line in interviews — it demonstrates:
//
// 1. Security     — CustomerId from JWT, not from request body
// 2. Performance  — one DB call for all products (batch fetch, no N+1)
// 3. Correctness  — stock check before decrement (InsufficientStockException)
// 4. Domain Model — Order as Aggregate Root (AddItem, AddDomainEvent)
// 5. Atomicity    — Order + OutboxMessage in ONE DB transaction (Outbox Pattern)
// 6. Events       — OrderPlacedEvent serialised to OutboxMessage for async delivery
//
// IN: What is the dual-write problem and how does this handler solve it?
// Dual-write: saving Order to DB AND publishing to Service Bus are two separate
// operations. If the publish fails after the save, the Order exists but the
// confirmation email is never sent — a consistency violation.
// Solution: save Order + OutboxMessage in ONE SQL transaction. The Outbox Processor
// Function reads OutboxMessages and publishes to Service Bus separately.
// If Service Bus is down, the Processor retries on next timer execution.
// Guaranteed at-least-once delivery with no message loss.

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

public class PlaceOrderCommandHandler
    : IRequestHandler<PlaceOrderCommand, Guid>
{
    private readonly IOrderRepository _orderRepository;
    private readonly IProductRepository _productRepository;
    private readonly IOutboxRepository _outboxRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserService _currentUserService;

    // IN: Five dependencies — all interfaces from Application layer.
    // This handler has zero knowledge of:
    // - EF Core (no DbContext, no DbSet)
    // - SQL Server (no connection strings, no SQL)
    // - ASP.NET Core (no HttpContext, no ClaimsPrincipal)
    // - Azure Service Bus (publishing is the Outbox Processor's job)
    // Pure application orchestration — the definition of the Application layer.
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

    public async Task<Guid> Handle(
        PlaceOrderCommand command,
        CancellationToken cancellationToken)
    {
        // =====================================================================
        // STEP 1: Extract and validate the authenticated user
        // =====================================================================

        // IN: CustomerId comes from the JWT claim, NOT from the request body.
        // This is a non-negotiable security decision. If the client could supply
        // CustomerId in the request, any authenticated user could place orders
        // on behalf of any other user — a trivial account takeover vector.
        // ICurrentUserService reads the "sub" claim from the validated JWT.
        // The JWT middleware has already verified the signature — these claims are trusted.
        var customerId = _currentUserService.UserId
            ?? throw new UnauthorizedAccessException(
                "You must be authenticated to place an order.");

        // Parse the string Identity ID to Guid — our domain uses Guid for CustomerId.
        // IN: ASP.NET Core Identity uses string IDs internally.
        // Our Order domain entity uses Guid. We parse here at the boundary —
        // the domain stays clean (Guid), Identity stays standard (string).
        if (!Guid.TryParse(customerId, out var customerGuid))
            throw new UnauthorizedAccessException(
                "Invalid user identity. Please log in again.");

        // =====================================================================
        // STEP 2: Batch-fetch all requested products in ONE DB query
        // =====================================================================

        var productIds = command.Items.Select(i => i.ProductId).ToList();

        // IN: GetByIdsAsync fetches ALL products in one SELECT ... WHERE Id IN (...)
        // The alternative — foreach item → GetByIdAsync — is an N+1 query.
        // For an order with 10 items, N+1 fires 10 SELECTs.
        // Batch fetch fires 1 SELECT with an IN clause.
        // At scale (thousands of concurrent orders) N+1 here is a DB killer.
        var products = await _productRepository
            .GetByIdsAsync(productIds, cancellationToken);

        // =====================================================================
        // STEP 3: Validate all products exist
        // =====================================================================

        // Build a dictionary for O(1) lookup — avoids nested O(n²) loops
        // IN: Dictionary lookup is O(1). Calling .FirstOrDefault(p => p.Id == x)
        // inside a loop is O(n) per item = O(n²) total for the validation loop.
        // For 50 items this is the difference between 50 lookups and 2,500 lookups.
        var productDict = products.ToDictionary(p => p.Id);

        foreach (var item in command.Items)
        {
            if (!productDict.ContainsKey(item.ProductId))
                // IN: NotFoundException is thrown if ANY product in the order
                // doesn't exist. The entire order fails — partial orders are not created.
                // This is the correct "all or nothing" behaviour for an order operation.
                throw new NotFoundException(nameof(Product), item.ProductId);
        }

        // =====================================================================
        // STEP 4: Validate stock and decrement atomically
        // =====================================================================

        // IN: Why validate ALL items before decrementing ANY?
        // If we validate-then-decrement item by item and fail on item 3,
        // items 1 and 2 have already had their stock decremented.
        // We'd need to roll them back — complex and error-prone.
        // Validate everything first, then decrement everything.
        // The DB transaction ensures the decrements are all-or-nothing anyway,
        // but this order of operations avoids partial state in the domain objects.
        foreach (var item in command.Items)
        {
            var product = productDict[item.ProductId];

            if (product.StockQuantity < item.Quantity)
                // IN: InsufficientStockException carries structured data:
                // ProductId, RequestedQuantity, AvailableQuantity.
                // UnhandledExceptionBehaviour logs these as structured properties —
                // searchable in Application Insights.
                // ExceptionMiddleware maps this to HTTP 400 Bad Request.
                throw new InsufficientStockException(
                    product.Id,
                    item.Quantity,
                    product.StockQuantity);
        }

        // All stock checks passed — now decrement
        // IN: Decrementing in-memory on tracked entities.
        // EF's change tracker detects these property changes.
        // When SaveChangesAsync fires, EF generates:
        // UPDATE Products SET StockQuantity = @newValue WHERE Id = @id
        // for every product whose StockQuantity changed.
        // All of these UPDATEs happen in the SAME transaction as the Order INSERT.
        foreach (var item in command.Items)
        {
            productDict[item.ProductId].StockQuantity -= item.Quantity;
        }

        // IN: Why not use UPDATE Products SET StockQuantity = StockQuantity - @qty
        // WHERE Id = @id AND StockQuantity >= @qty (optimistic concurrency)?
        // That is the correct production approach for high-concurrency scenarios.
        // It prevents race conditions where two concurrent orders both pass the
        // stock check but together over-decrement.
        // For a portfolio project, the EF change-tracker approach above is acceptable.
        // Production enhancement: use ExecuteUpdateAsync with a WHERE clause check
        // and verify rows affected == expected count. If 0 rows affected, another
        // request decremented first — throw InsufficientStockException and retry.

        // =====================================================================
        // STEP 5: Build the Order aggregate
        // =====================================================================

        // IN: Order is an Aggregate Root — it controls its own state.
        // We call order.AddItem() instead of order.Items.Add() because the
        // Aggregate Root pattern requires ALL state changes to go through
        // the aggregate's methods. Items has a private setter — external code
        // cannot bypass AddItem(). This enforces business invariants.
        var order = new Order
        {
            Id = Guid.NewGuid(),
            CustomerId = customerGuid,
            Status = OrderStatus.Pending,
            // TotalAmount is calculated by Order.AddItem() — not set here.
            // Setting it here would duplicate the calculation logic.
        };

        foreach (var item in command.Items)
        {
            var product = productDict[item.ProductId];

            order.AddItem(new OrderItem
            {
                Id = Guid.NewGuid(),
                OrderId = order.Id,
                ProductId = item.ProductId,
                Quantity = item.Quantity,

                // IN: UnitPrice is the price AT THIS MOMENT IN TIME.
                // Product.Price can change tomorrow — this order must always
                // show the price the customer agreed to pay.
                // This is the "price snapshot" pattern — store the price
                // on the line item, not as a reference to Product.Price.
                UnitPrice = product.Price
            });
        }

        // =====================================================================
        // STEP 6: Raise the domain event on the aggregate
        // =====================================================================

        // IN: AddDomainEvent() queues the event on the Order aggregate.
        // It is NOT published here. Two things happen with it:
        //
        // A) In-process (synchronous):
        //    AppDbContext.SaveChangesAsync() dispatches all domain events via
        //    MediatR.Publish() AFTER the DB save. In-process handlers (if any)
        //    receive the event immediately in the same request.
        //
        // B) Cross-process (asynchronous via Outbox):
        //    The OutboxMessage we create in Step 7 carries the same event payload
        //    to Azure Service Bus via the OutboxProcessorFunction. This is the
        //    reliable, guaranteed-delivery path for cross-service communication.
        //
        // IN: Why both in-process AND outbox?
        // In-process = immediate, same transaction, for local side effects.
        // Outbox = reliable, async, for cross-service/cross-process delivery.
        // They serve different purposes and can coexist.
        order.AddDomainEvent(new OrderPlacedEvent(
            order.Id,
            customerGuid,
            order.TotalAmount));

        // =====================================================================
        // STEP 7: Persist Order + OutboxMessage atomically
        // =====================================================================

        // Add the Order to the repository (enrols in EF change tracker)
        await _orderRepository.AddAsync(order, cancellationToken);

        // IN: This is the heart of the Outbox Pattern.
        // We serialise the domain event to JSON and store it as an OutboxMessage
        // IN THE SAME EF CHANGE TRACKER SESSION as the Order.
        // When SaveChangesAsync fires, BOTH are inserted in ONE SQL transaction.
        //
        // Scenario A — DB write succeeds: Order + OutboxMessage both committed.
        //   OutboxProcessorFunction reads the message and publishes to Service Bus.
        //
        // Scenario B — DB write fails: Neither Order NOR OutboxMessage committed.
        //   Transaction rolled back. No orphaned events, no lost orders. Clean.
        //
        // Scenario C — Service Bus publish fails after DB write:
        //   OutboxMessage.ProcessedAt remains null.
        //   OutboxProcessorFunction retries on next timer execution (10 seconds).
        //   At-least-once delivery guaranteed.
        var orderPlacedEvent = new OrderPlacedEvent(
            order.Id,
            customerGuid,
            order.TotalAmount);

        // IN: We create a second OrderPlacedEvent instance for the outbox
        // rather than reusing the one added to the aggregate's domain events.
        // The domain event on the aggregate is for in-process MediatR dispatch.
        // The OutboxMessage is for cross-process Service Bus delivery.
        // Keeping them separate makes each path explicit and independently testable.
        var outboxMessage = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            Type = typeof(OrderPlacedEvent).FullName!,
            // IN: JsonSerializer.Serialize — using System.Text.Json (built-in).
            // No Newtonsoft.Json dependency in Application layer.
            // The Type field tells the OutboxProcessor which class to deserialise to.
            Payload = JsonSerializer.Serialize(orderPlacedEvent),
            CreatedAt = DateTime.UtcNow
            // ProcessedAt = null — marks this as unprocessed for the Processor
        };

        await _outboxRepository.AddAsync(outboxMessage, cancellationToken);

        // In: This single SaveChangesAsync commits ALL of the following
        // in ONE SQL transaction:
        // - INSERT INTO Orders (...)
        // - INSERT INTO OrderItems (...) × N items
        // - UPDATE Products SET StockQuantity = ... × N products
        // - INSERT INTO OutboxMessages (...)
        //
        // If any one of these fails, ALL are rolled back.
        // This is exactly what Unit of Work is designed for.
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // =====================================================================
        // STEP 8: Return the new Order Id
        // =====================================================================

        // Client uses this to:
        // a) Navigate to the order confirmation page (GET /orders/{id})
        // b) Poll for order status updates
        // b) Display "Your order #XXXX has been placed" in the UI
        return order.Id;
    }
}
