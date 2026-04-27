// OrderItemConfiguration.cs — Fluent API config for OrderItem.
// INTERVIEW: OrderItem demonstrates the price snapshot pattern.
// UnitPrice is the price AT TIME OF ORDER — stored here, not referenced from Product.
// This is a critical business requirement: historical orders must never change price.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NexaStore.Domain.Entities;

namespace NexaStore.Persistence.Configurations;

public class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> builder)
    {
        // --- Table ---
        builder.ToTable("OrderItems");

        // --- Primary Key ---
        builder.HasKey(oi => oi.Id);

        // --- Properties ---
        builder.Property(oi => oi.Id)
            .IsRequired()
            .ValueGeneratedNever();

        builder.Property(oi => oi.OrderId)
            .IsRequired();

        builder.Property(oi => oi.ProductId)
            .IsRequired();

        builder.Property(oi => oi.Quantity)
            .IsRequired();

        // INTERVIEW: UnitPrice stored as decimal(18,2) — the snapshot of the
        // product price at the time this order was placed. Never update this field.
        builder.Property(oi => oi.UnitPrice)
            .IsRequired()
            .HasColumnType("decimal(18,2)");

        builder.Property(oi => oi.CreatedAt)
            .IsRequired();

        builder.Property(oi => oi.UpdatedAt)
            .IsRequired(false);

        // --- Constraints ---
        // Quantity must be at least 1 — cannot order zero items
        builder.ToTable(t => t.HasCheckConstraint(
            "CK_OrderItems_Quantity",
            "[Quantity] > 0"));

        // Price snapshot must be positive
        builder.ToTable(t => t.HasCheckConstraint(
            "CK_OrderItems_UnitPrice",
            "[UnitPrice] > 0"));

        // --- Indexes ---
        builder.HasIndex(oi => oi.OrderId)
            .HasDatabaseName("IX_OrderItems_OrderId");

        builder.HasIndex(oi => oi.ProductId)
            .HasDatabaseName("IX_OrderItems_ProductId");

        // --- Relationships ---
        // OrderItem → Product: Restrict delete — don't delete a product that
        // has been ordered. Soft-delete the product instead (future enhancement).
        // INTERVIEW: Restrict here is correct — deleting a product that appears
        // in historical orders would corrupt order history.
        builder.HasOne(oi => oi.Product)
            .WithMany()
            .HasForeignKey(oi => oi.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
