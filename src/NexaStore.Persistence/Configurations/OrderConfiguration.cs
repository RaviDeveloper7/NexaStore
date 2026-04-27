// OrderConfiguration.cs — Fluent API config for the Order aggregate.
// INTERVIEW: Order is the most complex entity in the system.
// Key decisions here: owned navigation for Items, enum storage as int,
// and Ignore for DomainEvents (they must never be persisted to this table).

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NexaStore.Domain.Entities;
using NexaStore.Domain.Enums;

namespace NexaStore.Persistence.Configurations;

public class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        // --- Table ---
        builder.ToTable("Orders");

        // --- Primary Key ---
        builder.HasKey(o => o.Id);

        // --- Properties ---
        builder.Property(o => o.Id)
            .IsRequired()
            .ValueGeneratedNever();

        builder.Property(o => o.CustomerId)
            .IsRequired();

        // INTERVIEW: Enum stored as int — more storage-efficient than string.
        // The downside is the DB column shows numbers, not names.
        // Trade-off: use a view or stored proc for human-readable reporting if needed.
        builder.Property(o => o.Status)
            .IsRequired()
            .HasConversion<int>()          // Store OrderStatus enum as int
            .HasDefaultValue(OrderStatus.Pending);

        builder.Property(o => o.TotalAmount)
            .IsRequired()
            .HasColumnType("decimal(18,2)");

        builder.Property(o => o.CreatedAt)
            .IsRequired();

        builder.Property(o => o.UpdatedAt)
            .IsRequired(false);

        // INTERVIEW: Ignore tells EF to never try to map DomainEvents to a DB column.
        // DomainEvents is a transient, in-memory collection — it has no place in the DB.
        // Without this, EF would throw an exception trying to map List<IDomainEvent>.
        builder.Ignore(o => o.DomainEvents);

        // --- Indexes ---
        // CustomerId is the most frequent filter — every customer query hits this
        builder.HasIndex(o => o.CustomerId)
            .HasDatabaseName("IX_Orders_CustomerId");

        // Status index — admin dashboard filters by status constantly
        builder.HasIndex(o => o.Status)
            .HasDatabaseName("IX_Orders_Status");

        // INTERVIEW: Composite index covers the most common admin query:
        // "show me all Pending orders for customer X" — single index scan.
        builder.HasIndex(o => new { o.CustomerId, o.Status })
            .HasDatabaseName("IX_Orders_CustomerId_Status");

        // CreatedAt index — for OrderExpiryFunction which queries:
        // WHERE Status = Pending AND CreatedAt < cutoffTime
        builder.HasIndex(o => o.CreatedAt)
            .HasDatabaseName("IX_Orders_CreatedAt");

        // --- Relationships ---
        // One Order → many OrderItems, cascade delete — items are meaningless without order
        // INTERVIEW: Cascade delete is appropriate here because OrderItem has no
        // independent existence — it's part of the Order aggregate.
        builder.HasMany(o => o.Items)
            .WithOne(i => i.Order)
            .HasForeignKey(i => i.OrderId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
