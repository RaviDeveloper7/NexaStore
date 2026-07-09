using Microsoft.EntityFrameworkCore.Storage;
using NexaStore.Application.Common.Interfaces.Persistence;
using NexaStore.Persistence.DatabaseContext;

namespace NexaStore.Persistence;

public class UnitOfWork : IUnitOfWork
{
    private readonly AppDbContext _context;
    private IDbContextTransaction? _transaction;

    public UnitOfWork(AppDbContext context)
    {
        _context = context;
    }

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        // Persists all tracked changes in one transaction and dispatches domain events.
        return await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        // IN: Explicit transaction for multi-SaveChanges scenarios with fine-grained control.
        _transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
    }

    public async Task CommitTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction is null)
            throw new InvalidOperationException(
                "Cannot commit — no active transaction. Call BeginTransactionAsync first.");

        try
        {
            await _transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await RollbackTransactionAsync(cancellationToken);
            throw;
        }
        finally
        {
            await _transaction.DisposeAsync();
            _transaction = null;
        }
    }

    public async Task RollbackTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction is null)
            throw new InvalidOperationException(
                "Cannot rollback — no active transaction. Call BeginTransactionAsync first.");

        await _transaction.RollbackAsync(cancellationToken);
    }

    public void Dispose()
    {
        // IN: Dispose explicit transaction only; DbContext lifetime managed by DI container.
        _transaction?.Dispose();
    }
}
