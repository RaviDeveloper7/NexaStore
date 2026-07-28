// MockOutboxRepository.cs — in-memory fake of IOutboxRepository.
// IN: Used by PlaceOrderCommandHandlerTests to verify the Outbox Pattern
// contract: an OutboxMessage of the correct Type must be added whenever
// an order is placed. This is how we PROVE the Outbox Pattern works
// without touching a real database or Service Bus.

using NexaStore.Application.Common.Interfaces.Services;
using NexaStore.Domain.Entities;

namespace NexaStore.Application.UnitTests.Mocks;

public class MockOutboxRepository : IOutboxRepository
{
    public List<OutboxMessage> Messages { get; } = new();

    public Task<IReadOnlyList<OutboxMessage>> GetUnprocessedAsync(
        int batchSize = 50, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<OutboxMessage>>(
            Messages.Where(m => m.ProcessedAt == null)
                    .OrderBy(m => m.CreatedAt)
                    .Take(batchSize)
                    .ToList());

    public Task MarkAsProcessedAsync(
        Guid messageId, CancellationToken cancellationToken = default)
    {
        var message = Messages.FirstOrDefault(m => m.Id == messageId);
        if (message is not null)
            message.ProcessedAt = DateTime.UtcNow;
        return Task.CompletedTask;
    }

    public Task AddAsync(
        OutboxMessage message, CancellationToken cancellationToken = default)
    {
        Messages.Add(message);
        return Task.CompletedTask;
    }
}
