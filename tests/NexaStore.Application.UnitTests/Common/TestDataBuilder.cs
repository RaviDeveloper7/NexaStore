// TestDataBuilder.cs — factory helpers for building valid test entities.
// IN: Centralising object creation avoids each test file duplicating
// "new Product { Id = ..., Name = ..., all 6 required properties }".
// If Product gains a new required property, only this builder needs updating —
// not every test that creates a Product. This is the "Object Mother" pattern,
// a well-known test data pattern for reducing test setup duplication.

using NexaStore.Domain.Entities;
using NexaStore.Domain.Enums;

namespace NexaStore.Application.UnitTests.Common;

public static class TestDataBuilder
{
    public static Category CreateCategory(
        Guid? id = null,
        string name = "Electronics")
        => new()
        {
            Id = id ?? Guid.NewGuid(),
            Name = name,
            CreatedAt = DateTime.UtcNow
        };

    public static Product CreateProduct(
        Guid? id = null,
        string name = "Test Product",
        decimal price = 99.99m,
        int stockQuantity = 10,
        Guid? categoryId = null)
        => new()
        {
            Id = id ?? Guid.NewGuid(),
            Name = name,
            Price = price,
            StockQuantity = stockQuantity,
            CategoryId = categoryId ?? Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow
        };

    public static Order CreateOrder(
        Guid? id = null,
        Guid? customerId = null,
        OrderStatus status = OrderStatus.Pending)
        => new()
        {
            Id = id ?? Guid.NewGuid(),
            CustomerId = customerId ?? Guid.NewGuid(),
            Status = status,
            CreatedAt = DateTime.UtcNow
        };
}
