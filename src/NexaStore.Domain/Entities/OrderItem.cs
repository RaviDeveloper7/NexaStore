// OrderItem.cs — a line item within an Order.
// INTERVIEW: OrderItem is NOT an Aggregate Root — it belongs to Order.
// It has no identity outside the context of its parent Order.
// UnitPrice is stored here (not referenced from Product) because product prices
// can change — we need the price AT THE TIME OF ORDER, not today's price.

namespace NexaStore.Domain.Entities;

public class OrderItem : BaseEntity
{
    // Foreign key back to the parent Order aggregate
    public Guid OrderId { get; set; }

    // Navigation back to parent — EF needs this for cascade delete config
    public Order Order { get; set; } = null!;

    // Which product was ordered
    public Guid ProductId { get; set; }

    // Navigation to product — used by EF for JOIN queries
    public Product Product { get; set; } = null!;

    // How many units were ordered
    public int Quantity { get; set; }

    // INTERVIEW: Price snapshot at time of order — critical business requirement.
    // If Product.Price changes tomorrow, this order still shows the correct price.
    // This is a common interview question about immutable order history.
    public decimal UnitPrice { get; set; }
}
