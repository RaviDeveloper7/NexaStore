// CancelOrderCommandHandlerTests.cs — proves the state machine, ownership
// enforcement, stock restoration, and idempotency of order cancellation.

using FluentAssertions;
using Moq;
using NexaStore.Application.Common.Interfaces.Identity;
using NexaStore.Application.Features.Orders.Commands.CancelOrder;
using NexaStore.Application.UnitTests.Common;
using NexaStore.Application.UnitTests.Mocks;
using NexaStore.Domain.Entities;
using NexaStore.Domain.Enums;
using NexaStore.Domain.Exceptions;
using Xunit;

namespace NexaStore.Application.UnitTests.Features.Orders;

public class CancelOrderCommandHandlerTests
{
    private readonly MockOrderRepository _orderRepository = new();
    private readonly MockOutboxRepository _outboxRepository = new();
    private readonly MockUnitOfWork _unitOfWork = new();
    private readonly Mock<ICurrentUserService> _currentUserServiceMock = new();

    private readonly Guid _customerId = Guid.NewGuid();

    public CancelOrderCommandHandlerTests()
    {
        // Default: authenticated as the order-owning Customer, not Admin
        _currentUserServiceMock.Setup(x => x.UserId).Returns(_customerId.ToString());
        _currentUserServiceMock.Setup(x => x.IsAdmin).Returns(false);
    }

    private CancelOrderCommandHandler CreateHandler() => new(
        _orderRepository,
        _outboxRepository,
        _unitOfWork,
        _currentUserServiceMock.Object);

    [Fact]
    public async Task Handle_PendingOrderOwnedByCaller_CancelsAndRestoresStock()
    {
        // Arrange
        var product = TestDataBuilder.CreateProduct(stockQuantity: 5);
        var order = TestDataBuilder.CreateOrder(
            customerId: _customerId, status: OrderStatus.Pending);

        var orderItem = new OrderItem
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            ProductId = product.Id,
            Product = product,
            Quantity = 3,
            UnitPrice = product.Price
        };
        order.AddItem(orderItem);

        _orderRepository.Orders.Add(order);

        var handler = CreateHandler();
        var command = new CancelOrderCommand(order.Id, "Changed my mind");

        // Act
        await handler.Handle(command, CancellationToken.None);

        // Assert
        order.Status.Should().Be(OrderStatus.Cancelled);

        // IN: Stock restoration is the critical business behaviour here —
        // 5 (current) + 3 (restored from the cancelled item) = 8
        product.StockQuantity.Should().Be(8);

        _unitOfWork.SaveChangesCallCount.Should().Be(1);
        _outboxRepository.Messages.Should().ContainSingle();
    }

    [Fact]
    public async Task Handle_OrderOwnedByDifferentCustomer_ThrowsUnauthorizedAccessException()
    {
        // Arrange — order belongs to someone else
        var otherCustomerId = Guid.NewGuid();
        var order = TestDataBuilder.CreateOrder(
            customerId: otherCustomerId, status: OrderStatus.Pending);
        _orderRepository.Orders.Add(order);

        var handler = CreateHandler();
        var command = new CancelOrderCommand(order.Id);

        // Act
        var act = async () => await handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<UnauthorizedAccessException>();

        // Order must remain untouched
        order.Status.Should().Be(OrderStatus.Pending);
        _unitOfWork.SaveChangesCallCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_AdminCancellingAnyOrder_BypassesOwnershipCheck()
    {
        // Arrange — caller is Admin, order belongs to a different customer
        _currentUserServiceMock.Setup(x => x.IsAdmin).Returns(true);

        var order = TestDataBuilder.CreateOrder(
            customerId: Guid.NewGuid(), status: OrderStatus.Confirmed);
        _orderRepository.Orders.Add(order);

        var handler = CreateHandler();
        var command = new CancelOrderCommand(order.Id, "Admin override");

        // Act
        await handler.Handle(command, CancellationToken.None);

        // Assert — no exception thrown, cancellation succeeded
        order.Status.Should().Be(OrderStatus.Cancelled);
    }

    [Theory]
    [InlineData(OrderStatus.Shipped)]
    [InlineData(OrderStatus.Delivered)]
    public async Task Handle_NonCancellableStatus_ThrowsBadRequestException(
        OrderStatus status)
    {
        // IN: [Theory] + [InlineData] runs the same test body against multiple
        // inputs — proves the state machine rejects cancellation from EVERY
        // non-cancellable status, not just one hardcoded example.
        var order = TestDataBuilder.CreateOrder(
            customerId: _customerId, status: status);
        _orderRepository.Orders.Add(order);

        var handler = CreateHandler();
        var command = new CancelOrderCommand(order.Id);

        // Act
        var act = async () => await handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<BadRequestException>();
        order.Status.Should().Be(status); // unchanged
    }

    [Fact]
    public async Task Handle_AlreadyCancelledOrder_IsIdempotentAndDoesNotThrow()
    {
        // Arrange
        var order = TestDataBuilder.CreateOrder(
            customerId: _customerId, status: OrderStatus.Cancelled);
        _orderRepository.Orders.Add(order);

        var handler = CreateHandler();
        var command = new CancelOrderCommand(order.Id);

        // Act
        var act = async () => await handler.Handle(command, CancellationToken.None);

        // Assert — IN: idempotency means NO exception on a retry, and NO
        // duplicate side effects (no second Outbox message, no second save).
        await act.Should().NotThrowAsync();

        _unitOfWork.SaveChangesCallCount.Should().Be(0);
        _outboxRepository.Messages.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_OrderDoesNotExist_ThrowsNotFoundException()
    {
        // Arrange — no orders seeded
        var handler = CreateHandler();
        var command = new CancelOrderCommand(Guid.NewGuid());

        // Act
        var act = async () => await handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>();
    }
}
