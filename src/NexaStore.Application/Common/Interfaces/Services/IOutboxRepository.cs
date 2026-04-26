// IOutboxRepository.cs — contract for reading and updating OutboxMessages.
// INTERVIEW: OutboxMessage does NOT extend BaseEntity so it doesn't fit
// IGenericRepository<T>. It gets its own specific repository interface.
// This is used exclusively by OutboxProcessorFunction in the Functions project.

using NexaStore.Domain.Entities;

namespace NexaStore.Application.Common.Interfaces.Services;

public interface IOutboxRepository
{
    // Fetch all unprocessed messages — WHERE ProcessedAt IS NULL
    // INTERVIEW: The processor reads these, publishes to Service Bus,
    // then marks them processed. Batch size limits DB load per execution.
    Task<IReadOnlyList<OutboxMessage>> GetUnprocessedAsync(
        int batchSize = 50,
        CancellationToken cancellationToken = default);

    // Mark a single message as processed — sets ProcessedAt = UtcNow
    // Called after successful Service Bus publish
    Task MarkAsProcessedAsync(
        Guid messageId,
        CancellationToken cancellationToken = default);

    // Add a new outbox message — called from PlaceOrderCommandHandler
    // within the same EF transaction as the Order save
    Task AddAsync(
        OutboxMessage message,
        CancellationToken cancellationToken = default);
}
