// OutboxRepository.cs — manages reading and updating OutboxMessages.
// INTERVIEW: OutboxMessage does NOT extend BaseEntity so it cannot use
// GenericRepository<T> which has a constraint of where T : BaseEntity.
// It gets its own dedicated repository — a deliberate design choice.
//
// This repository has three responsibilities:
// 1. AddAsync       — called from PlaceOrderCommandHandler (same EF transaction as Order)
// 2. GetUnprocessed — called from OutboxProcessorFunction every 10 seconds
// 3. MarkAsProcessed — called after successful Service Bus publish

using Microsoft.EntityFrameworkCore;
using NexaStore.Application.Common.Interfaces.Services;
using NexaStore.Domain.Entities;
using NexaStore.Persistence.DatabaseContext;

namespace NexaStore.Persistence.Repositories;

public class OutboxRepository : IOutboxRepository
{
    private readonly AppDbContext _context;

    public OutboxRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task AddAsync(
        OutboxMessage message,
        CancellationToken cancellationToken = default)
    {
        // INTERVIEW: This is called inside PlaceOrderCommandHandler, which already
        // has an open EF change-tracking session. Adding the OutboxMessage here
        // enrols it in the SAME DbContext instance — meaning it will be persisted
        // in the exact same SQL transaction as the Order when SaveChangesAsync fires.
        // This is the atomic guarantee the Outbox Pattern depends on.
        // If the DB transaction rolls back, both the Order AND the OutboxMessage
        // are rolled back together. No orphaned events, no lost events.
        await _context.OutboxMessages.AddAsync(message, cancellationToken);
    }

    public async Task<IReadOnlyList<OutboxMessage>> GetUnprocessedAsync(
        int batchSize = 50,
        CancellationToken cancellationToken = default)
    {
        // INTERVIEW: Batch size cap is critical for the timer function.
        // Without it, if 10,000 orders pile up (e.g. after an outage), the processor
        // would try to publish all 10,000 messages in one function execution,
        // likely timing out. Processing in batches of 50 means:
        // - Each execution is fast and predictable
        // - The function scales naturally — next execution picks up the next batch
        // - Service Bus is not flooded with burst traffic

        // WHERE ProcessedAt IS NULL — hits the filtered index from OutboxMessageConfiguration
        // ORDER BY CreatedAt ASC — process events in chronological order
        // INTERVIEW: Ordering by CreatedAt guarantees events are delivered to Service Bus
        // in the order they were created. Out-of-order delivery would mean
        // OrderCancelledEvent arriving before OrderPlacedEvent — a consumer nightmare.
        return await _context.OutboxMessages
            .Where(m => m.ProcessedAt == null)
            .OrderBy(m => m.CreatedAt)
            .Take(batchSize)
            .ToListAsync(cancellationToken);
    }

    public async Task MarkAsProcessedAsync(
        Guid messageId,
        CancellationToken cancellationToken = default)
    {
        // INTERVIEW: ExecuteUpdateAsync is an EF Core 7+ feature that generates
        // a direct UPDATE statement without loading the entity into memory first.
        //
        // The traditional approach:
        //   var msg = await FindAsync(messageId);
        //   msg.ProcessedAt = DateTime.UtcNow;
        //   await SaveChangesAsync();
        //
        // That fires SELECT then UPDATE — two round-trips, entity in memory.
        //
        // ExecuteUpdateAsync fires a single:
        //   UPDATE OutboxMessages SET ProcessedAt = @now WHERE Id = @id
        //
        // For a function that processes 50 messages every 10 seconds,
        // this halves the DB round-trips — a meaningful throughput gain.
        await _context.OutboxMessages
            .Where(m => m.Id == messageId)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    m => m.ProcessedAt,
                    DateTime.UtcNow),
                cancellationToken);
    }
}
