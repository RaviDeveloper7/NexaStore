// OutboxMessageConfiguration.cs — Fluent API config for OutboxMessage.
// INTERVIEW: OutboxMessage is a pure infrastructure table — it's not a domain
// entity in the business sense, but it lives in the same DB for atomic writes.
// The entire value of the Outbox Pattern depends on this table being in the
// SAME DATABASE TRANSACTION as the Order — which EF Core gives us for free.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NexaStore.Domain.Entities;

namespace NexaStore.Persistence.Configurations;

public class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        // --- Table ---
        builder.ToTable("OutboxMessages");

        // --- Primary Key ---
        // INTERVIEW: OutboxMessage does not extend BaseEntity — its own Guid Id.
        builder.HasKey(o => o.Id);

        builder.Property(o => o.Id)
            .IsRequired()
            .ValueGeneratedNever();

        // The fully qualified event type name — used by the processor to deserialise
        // e.g. "NexaStore.Domain.Events.OrderPlacedEvent"
        builder.Property(o => o.Type)
            .IsRequired()
            .HasMaxLength(500);

        // JSON payload — could be large for complex events, nvarchar(max) is appropriate
        // INTERVIEW: nvarchar(max) is fine here — this table is append-only,
        // rows are deleted or never queried after ProcessedAt is set.
        builder.Property(o => o.Payload)
            .IsRequired()
            .HasColumnType("nvarchar(max)");

        builder.Property(o => o.CreatedAt)
            .IsRequired();

        // ProcessedAt = null means unprocessed — the processor's WHERE clause
        builder.Property(o => o.ProcessedAt)
            .IsRequired(false);

        // --- Indexes ---
        // INTERVIEW: This index is the single most important one in the entire schema.
        // The OutboxProcessorFunction runs every 10 seconds and fires:
        // SELECT TOP 50 * FROM OutboxMessages WHERE ProcessedAt IS NULL ORDER BY CreatedAt
        // Without this filtered index that query does a full table scan every 10 seconds.
        // HasFilter("[ProcessedAt] IS NULL") creates a filtered index — only unprocessed
        // rows are indexed, keeping the index tiny and the query blazing fast.
        builder.HasIndex(o => o.ProcessedAt)
            .HasFilter("[ProcessedAt] IS NULL")
            .HasDatabaseName("IX_OutboxMessages_Unprocessed");

        // CreatedAt for ordering — ensures events are processed in chronological order
        builder.HasIndex(o => o.CreatedAt)
            .HasDatabaseName("IX_OutboxMessages_CreatedAt");
    }
}
