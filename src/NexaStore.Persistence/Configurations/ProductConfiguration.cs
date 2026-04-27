// ProductConfiguration.cs — Fluent API config for the Product entity.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NexaStore.Domain.Entities;

namespace NexaStore.Persistence.Configurations;

public class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        // --- Table ---
        builder.ToTable("Products");

        // --- Primary Key ---
        builder.HasKey(p => p.Id);

        // --- Properties ---
        builder.Property(p => p.Id)
            .IsRequired()
            .ValueGeneratedNever();

        builder.Property(p => p.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(p => p.Description)
            .HasMaxLength(2000);   // Product descriptions can be lengthy

        // INTERVIEW: HasColumnType("decimal(18,2)") is critical for money fields.
        // Without this EF will use decimal(18,4) by default, which wastes storage.
        // 18,2 means up to 9,999,999,999,999,999.99 — more than sufficient for prices.
        // Never use float or real for money — they are approximate binary representations.
        builder.Property(p => p.Price)
            .IsRequired()
            .HasColumnType("decimal(18,2)");

        builder.Property(p => p.StockQuantity)
            .IsRequired()
            .HasDefaultValue(0);   // Sensible default — no negative stock at creation

        builder.Property(p => p.CategoryId)
            .IsRequired();

        builder.Property(p => p.CreatedAt)
            .IsRequired();

        builder.Property(p => p.UpdatedAt)
            .IsRequired(false);

        // --- Indexes ---
        // Name lookup for search — not unique, same product name could appear
        // in different categories (e.g. "Basic Plan" in two categories)
        builder.HasIndex(p => p.Name)
            .HasDatabaseName("IX_Products_Name");

        // Category filter index — GetProducts filters by CategoryId frequently
        builder.HasIndex(p => p.CategoryId)
            .HasDatabaseName("IX_Products_CategoryId");

        // INTERVIEW: Composite index on (CategoryId, Name) covers the common
        // query pattern: "get all products in category X sorted by name".
        // This index satisfies both the WHERE and ORDER BY in one scan.
        builder.HasIndex(p => new { p.CategoryId, p.Name })
            .HasDatabaseName("IX_Products_CategoryId_Name");

        // --- Constraints ---
        // INTERVIEW: Check constraints enforce business rules at the DB level —
        // a second line of defense after application validation.
        // Price cannot be zero or negative — a product must have a price.
        builder.ToTable(t => t.HasCheckConstraint(
            "CK_Products_Price",
            "[Price] > 0"));

        // StockQuantity cannot be negative — you can't have -5 items in stock
        builder.ToTable(t => t.HasCheckConstraint(
            "CK_Products_StockQuantity",
            "[StockQuantity] >= 0"));
    }
}
