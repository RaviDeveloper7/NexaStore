// MockUnitOfWork.cs — in-memory fake of IUnitOfWork.
// IN: The fake does almost nothing — mock repositories already applied
// changes directly to their in-memory lists (AddAsync adds immediately,
// property mutations happen in-place). SaveChangesAsync in the fake is
// purely a counter — tests can assert it was called the expected number
// of times, proving the handler correctly commits its unit of work.

namespace NexaStore.Application.UnitTests.Mocks;

public class MockUnitOfWork : NexaStore.Application.Common.Interfaces.Persistence.IUnitOfWork
{
    // IN: Tracks how many times SaveChangesAsync was called.
    // Tests assert SaveChangesCallCount == 1 to prove the handler
    // commits exactly once — not zero times (forgot to save) and not
    // multiple times (unnecessary extra round-trips).
    public int SaveChangesCallCount { get; private set; }

    public bool TransactionBegun { get; private set; }
    public bool TransactionCommitted { get; private set; }
    public bool TransactionRolledBack { get; private set; }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SaveChangesCallCount++;
        // IN: Return 1 — simulates "1 row affected". Handlers rarely check
        // the return value, but returning a realistic number avoids surprises
        // if a handler is later changed to assert on it.
        return Task.FromResult(1);
    }

    public Task BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        TransactionBegun = true;
        return Task.CompletedTask;
    }

    public Task CommitTransactionAsync(CancellationToken cancellationToken = default)
    {
        TransactionCommitted = true;
        return Task.CompletedTask;
    }

    public Task RollbackTransactionAsync(CancellationToken cancellationToken = default)
    {
        TransactionRolledBack = true;
        return Task.CompletedTask;
    }

    public void Dispose() { }
}
