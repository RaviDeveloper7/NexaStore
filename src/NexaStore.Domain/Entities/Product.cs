// Product.cs — core catalog entity. Holds stock, price, and category linkage.
// INTERVIEW: Price is decimal, never float or double — floating point errors
// in financial calculations are a production bug. Decimal is exact.

namespace NexaStore.Domain.Entities;

public class Product : BaseEntity
{
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    // INTERVIEW: decimal for money — float/double lose precision with rounding
    public decimal Price { get; set; }

    // Tracks available inventory — decremented in PlaceOrderCommandHandler
    public int StockQuantity { get; set; }

    // Foreign key — links product to its category
    public Guid CategoryId { get; set; }

    // Navigation property — allows EF to load related Category
    public Category Category { get; set; } = null!;
}
