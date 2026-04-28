// PaymentConfiguration.cs — Fluent API config for Payment.
// INTERVIEW: Payment has a 1:1 relationship with Order in the current design.
// The FK is on Payment (OrderId), not on Order — Order has no PaymentId.
// This means you can query payments independently without loading orders.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NexaStore.Domain.Entities;
using NexaStore.Domain.Enums;

namespace NexaStore.Persistence.Configurations;

public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        // --- Table ---
        builder.ToTable("Payments");

        // --- Primary Key ---
        builder.HasKey(p => p.Id);

        // --- Properties ---
        builder.Property(p => p.Id)
            .IsRequired()
            .ValueGeneratedNever();

        builder.Property(p => p.OrderId)
            .IsRequired();

        builder.Property(p => p.Amount)
            .IsRequired()
            .HasColumnType("decimal(18,2)");

        builder.Property(p => p.Status)
            .IsRequired()
            .HasConversion<int>();

        // Method stored as string — e.g. "CreditCard", "PayPal", "BankTransfer"
        // MaxLength 50 is more than enough for any payment method name
        builder.Property(p => p.Method)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(p => p.CreatedAt)
            .IsRequired();

        builder.Property(p => p.UpdatedAt)
            .IsRequired(false);

        // --- Constraints ---
        builder.ToTable(t => t.HasCheckConstraint(
            "CK_Payments_Amount",
            "[Amount] > 0"));

        // --- Indexes ---
        // INTERVIEW: Unique index on OrderId — each order has at most one payment record.
        // If you need multiple payment attempts, remove IsUnique and add an attempt counter.
        builder.HasIndex(p => p.OrderId)
            .IsUnique()
            .HasDatabaseName("IX_Payments_OrderId");

        builder.HasIndex(p => p.Status)
            .HasDatabaseName("IX_Payments_Status");

        // --- Relationships ---
        // Payment → Order: one-to-one via OrderId FK on Payment
        // Restrict delete — don't delete an order that has a payment record
        builder.HasOne(p => p.Order)
            .WithMany()
            .HasForeignKey(p => p.OrderId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
