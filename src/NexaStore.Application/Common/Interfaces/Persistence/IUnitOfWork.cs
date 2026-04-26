// IUnitOfWork.cs — coordinates saving all changes in a single DB transaction.
// INTERVIEW: This is one of the most asked-about patterns in .NET interviews.
// The Unit of Work wraps multiple repository operations into one atomic transaction.
// PlaceOrderCommandHandler: Add Order → Add OutboxMessage → SaveChangesAsync().
// Both succeed or both fail together. This is how the Outbox Pattern stays atomic.

namespace NexaStore.Application.Common.Interfaces.Persistence;

public interface IUnitOfWork : IDisposable
{
    // Commits all pending changes tracked by EF Core in a single transaction.
    // Returns the number of rows affected.
    // INTERVIEW: All handlers call SaveChangesAsync() via IUnitOfWork, never
    // via DbContext directly. This keeps handlers unaware of EF Core's existence.
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    // Explicitly begin a transaction — used when you need finer control
    // e.g. PlaceOrder needs to guarantee Order + OutboxMessage save atomically
    // INTERVIEW: Most of the time EF's default transaction-per-SaveChanges is fine.
    // BeginTransactionAsync is for cases where you need to span multiple SaveChanges.
    Task BeginTransactionAsync(CancellationToken cancellationToken = default);

    // Commit the explicit transaction
    Task CommitTransactionAsync(CancellationToken cancellationToken = default);

    // Roll back if anything fails — called in catch block
    Task RollbackTransactionAsync(CancellationToken cancellationToken = default);
}
