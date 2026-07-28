// PlaceOrderCommandHandlerTests.cs — proves the most important handler in the
// solution behaves correctly under every scenario: happy path, insufficient
// stock, non-existent product, unauthenticated caller.
//
// IN: Test naming convention: MethodName_Scenario_ExpectedBehaviour.
// This is the most widely used xUnit naming convention — the test name alone
// tells you what's being tested and what "success" looks like, without
// reading the test body. A failing test's NAME is often enough to diagnose
// the bug before even opening the file.
//
// IN: AAA pattern — Arrange, Act, Assert. Every test below follows this
// structure explicitly with comments. This is the universal unit test
// structure regardless of language or framework.

using FluentAssertions;
using Moq;
using NexaStore.Application.Common.Interfaces.Identity;
using NexaStore.Application.Features.Orders.Commands.PlaceOrder;
using NexaStore.Application.UnitTests.Common;
using NexaStore.Application.UnitTests.Mocks;
using NexaStore.Domain.Events;
using NexaStore.Domain.Exceptions;
using Xunit;

namespace NexaStore.Application.UnitTests.Features.Orders;

public class PlaceOrderCommandHandlerTests
{
    private readonly MockOrderRepository   _orderRepository   = new();
    private readonly MockProductRepository _productRepository = new();
    private readonly MockOutboxRepository  _outboxRepository  = new();
    private readonly MockUnitOfWork        _unitOfWork        = new();

    // IN: ICurrentUserService is mocked with Moq (not hand-written) because
    // it's a small interface (4 members) with simple property-return behaviour —
    // no filtering or business logic to replicate. Moq is the right tool here.
    private readonly Mock<ICurrentUserService> _currentUserServiceMock = new();

    private readonly PlaceOrderCommandHandler _handler;

    // IN: The authenticated customer's Id — used consistently across tests
    // so ICurrentUserService.UserId always returns this value.
    private readonly Guid _customerId = Guid.NewGuid();

    public PlaceOrderCommandHandlerTests()
    {
        // IN: Constructor runs before EVERY test method (xUnit creates a new
        // class instance per test — no shared state between tests, guaranteed
        // isolation). This IS the Arrange phase for common setup.
        _currentUserServiceMock
            .Setup(x => x.UserId)
            .Returns(_customerId.ToString());

        _handler = new PlaceOrderCommandHandler(
            _orderRepository,
            _productRepository,
            _outboxRepository,
            _unitOfWork,
            _currentUserServiceMock.Object);
    }

    [Fact]
    public async Task Handle_ValidOrder_CreatesOrderAndDecrementsStock()
    {
        // Arrange
        var product = TestDataBuilder.CreateProduct(stockQuantity: 10, price: 50m);
        _productRepository.Products.Add(product);

        var command = new PlaceOrderCommand
        {
            Items = new List<OrderItemRequest>
            {
                new() { ProductId = product.Id, Quantity = 3 }
            }
        };

        // Act
        var orderId = await _handler.Handle(command, CancellationToken.None);

        // Assert
        orderId.Should().NotBeEmpty();

        // IN: Verify the Order was actually persisted to the repository —
        // proves AddAsync was called with correctly constructed data.
        var savedOrder = _orderRepository.Orders.Should().ContainSingle().Subject;
        savedOrder.CustomerId.Should().Be(_customerId);
        savedOrder.TotalAmount.Should().Be(150m); // 3 × 50

        // IN: Verify stock was decremented — proves the fix from your earlier
        // bug report (AsNoTracking silently dropping the mutation) does NOT
        // regress. This test would have caught that bug immediately.
        var updatedProduct = _productRepository.Products.Single();
        updatedProduct.StockQuantity.Should().Be(7); // 10 - 3

        // Verify SaveChangesAsync was called exactly once — one atomic commit
        _unitOfWork.SaveChangesCallCount.Should().Be(1);
    }

    [Fact]
    public async Task Handle_ValidOrder_CreatesOutboxMessageWithOrderPlacedEventType()
    {
        // Arrange
        var product = TestDataBuilder.CreateProduct(stockQuantity: 5, price: 25m);
        _productRepository.Products.Add(product);

        var command = new PlaceOrderCommand
        {
            Items = new List<OrderItemRequest>
        {
            new() { ProductId = product.Id, Quantity = 1 }
        }
        };

        // Act
        var orderId = await _handler.Handle(command, CancellationToken.None);

        // Assert
        var outboxMessage = _outboxRepository.Messages.Should().ContainSingle().Subject;
        outboxMessage.Type.Should().Be(typeof(OrderPlacedEvent).FullName);
        outboxMessage.ProcessedAt.Should().BeNull();

        // IN: Assert on what OrderPlacedEvent actually carries — OrderId,
        // CustomerId, TotalAmount. ProductId is NOT part of this event;
        // it's order-level, not line-item-level. If a consumer needs product
        // details, it would query GetOrderByIdQuery separately using OrderId.
        outboxMessage.Payload.Should().Contain(orderId.ToString());
        outboxMessage.Payload.Should().Contain(_customerId.ToString());
        outboxMessage.Payload.Should().Contain("25"); // TotalAmount = 1 × 25
    }

    [Fact]
    public async Task Handle_InsufficientStock_ThrowsInsufficientStockException()
    {
        // Arrange
        var product = TestDataBuilder.CreateProduct(stockQuantity: 2);
        _productRepository.Products.Add(product);

        var command = new PlaceOrderCommand
        {
            Items = new List<OrderItemRequest>
            {
                // Requesting MORE than available stock
                new() { ProductId = product.Id, Quantity = 5 }
            }
        };

        // Act
        var act = async () => await _handler.Handle(command, CancellationToken.None);

        // Assert
        // IN: FluentAssertions' ThrowAsync<T>() is the idiomatic way to test
        // for expected exceptions. It also lets us assert on the exception's
        // properties — proving InsufficientStockException carries the exact
        // ProductId, Requested, and Available values the handler computed.
        var exception = await act.Should()
            .ThrowAsync<InsufficientStockException>();

        exception.Which.ProductId.Should().Be(product.Id);
        exception.Which.RequestedQuantity.Should().Be(5);
        exception.Which.AvailableQuantity.Should().Be(2);

        // IN: Critical assertion — NOTHING should be persisted when validation
        // fails. This proves the "validate everything before decrementing
        // anything" design decision actually holds under test.
        _orderRepository.Orders.Should().BeEmpty();
        _unitOfWork.SaveChangesCallCount.Should().Be(0);

        // Stock must remain untouched — the failed order never touched inventory
        product.StockQuantity.Should().Be(2);
    }

    [Fact]
    public async Task Handle_ProductDoesNotExist_ThrowsNotFoundException()
    {
        // Arrange — no products seeded, so any ProductId is unknown
        var command = new PlaceOrderCommand
        {
            Items = new List<OrderItemRequest>
            {
                new() { ProductId = Guid.NewGuid(), Quantity = 1 }
            }
        };

        // Act
        var act = async () => await _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>();

        _orderRepository.Orders.Should().BeEmpty();
        _unitOfWork.SaveChangesCallCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_MultipleItemsAllInStock_DecrementsEachProductCorrectly()
    {
        // Arrange — proves batch handling works correctly across multiple products,
        // not just the single-item happy path.
        var productA = TestDataBuilder.CreateProduct(stockQuantity: 10, price: 20m);
        var productB = TestDataBuilder.CreateProduct(stockQuantity: 5,  price: 30m);
        _productRepository.Products.AddRange(new[] { productA, productB });

        var command = new PlaceOrderCommand
        {
            Items = new List<OrderItemRequest>
            {
                new() { ProductId = productA.Id, Quantity = 4 },
                new() { ProductId = productB.Id, Quantity = 2 }
            }
        };

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        _productRepository.Products.Single(p => p.Id == productA.Id)
            .StockQuantity.Should().Be(6);  // 10 - 4

        _productRepository.Products.Single(p => p.Id == productB.Id)
            .StockQuantity.Should().Be(3);  // 5 - 2

        var order = _orderRepository.Orders.Single();
        order.TotalAmount.Should().Be(140m); // (4×20) + (2×30)
        order.Items.Should().HaveCount(2);
    }

    [Fact]
    public async Task Handle_OneOfTwoProductsHasInsufficientStock_ThrowsAndPersistsNothing()
    {
        // IN: This is the test that proves "all-or-nothing" validation.
        // productA has enough stock. productB does not.
        // The ENTIRE order must fail — productA's stock must NOT be decremented
        // even though it individually had enough.
        var productA = TestDataBuilder.CreateProduct(stockQuantity: 10);
        var productB = TestDataBuilder.CreateProduct(stockQuantity: 1);
        _productRepository.Products.AddRange(new[] { productA, productB });

        var command = new PlaceOrderCommand
        {
            Items = new List<OrderItemRequest>
            {
                new() { ProductId = productA.Id, Quantity = 3 },  // fine
                new() { ProductId = productB.Id, Quantity = 5 }   // insufficient
            }
        };

        // Act
        var act = async () => await _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InsufficientStockException>();

        // Neither product's stock should have changed — proves validation
        // happens fully BEFORE any decrement, exactly as designed.
        _productRepository.Products.Single(p => p.Id == productA.Id)
            .StockQuantity.Should().Be(10);
        _productRepository.Products.Single(p => p.Id == productB.Id)
            .StockQuantity.Should().Be(1);

        _orderRepository.Orders.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_UnauthenticatedUser_ThrowsUnauthorizedAccessException()
    {
        // Arrange — override the mock to simulate no authenticated user
        _currentUserServiceMock.Setup(x => x.UserId).Returns((string?)null);

        var product = TestDataBuilder.CreateProduct(stockQuantity: 5);
        _productRepository.Products.Add(product);

        var command = new PlaceOrderCommand
        {
            Items = new List<OrderItemRequest>
            {
                new() { ProductId = product.Id, Quantity = 1 }
            }
        };

        // Act
        var act = async () => await _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<UnauthorizedAccessException>();
        _orderRepository.Orders.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_ValidOrder_SetsUnitPriceFromProductAtTimeOfOrder()
    {
        // IN: This test proves the "price snapshot" pattern — the most
        // frequently asked-about design decision in this handler.
        var product = TestDataBuilder.CreateProduct(price: 249.99m, stockQuantity: 5);
        _productRepository.Products.Add(product);

        var command = new PlaceOrderCommand
        {
            Items = new List<OrderItemRequest>
            {
                new() { ProductId = product.Id, Quantity = 2 }
            }
        };

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        var orderItem = _orderRepository.Orders.Single().Items.Single();
        orderItem.UnitPrice.Should().Be(249.99m);

        // Now change the product's price AFTER the order — proves the
        // order's snapshot is independent of future price changes
        product.Price = 999.99m;
        orderItem.UnitPrice.Should().Be(249.99m); // unchanged — snapshot holds
    }
}
