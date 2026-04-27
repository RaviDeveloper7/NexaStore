// UnitOfWork.cs — coordinates all repository operations into a single DB transaction.
// INTERVIEW: This is THE most commonly discussed pattern in senior .NET interviews.
// Be ready to explain:
//
// Q: Why do you need UnitOfWork if EF Core's DbContext already tracks changes?
// A: DbContext IS a Unit of Work internally. IUnitOfWork is our APPLICATION-LEVEL
//    abstraction over it. This keeps handlers decoupled from EF Core — handlers
//    only know about IUnitOfWork, not DbContext. This makes handlers testable
//    by mocking IUnitOfWork without spinning up a real database.
//
// Q: Why not just inject DbContext directly into handlers?
// A: Injecting DbContext directly couples your Application layer to EF Core.
//    That breaks Clean Architecture — Application should not depend on Persistence.
//    IUnitOfWork is defined in Application.Common.Interfaces, implemented here.
//
// Q: When would you use BeginTransactionAsync vs just SaveChangesAsync?
// A: SaveChangesAsync wraps all pending changes in one implicit transaction.
//    BeginTransactionAsync is for cases where you need to span MULTIPLE SaveChanges
//    calls in one transaction — e.g. save, do external work, then save again.
//    In NexaStore: PlaceOrder uses the implicit transaction (single SaveChanges).
//    BeginTransactionAsync is available for more complex scenarios.

using Microsoft.EntityFrameworkCore.Storage;
using NexaStore.Application.Common.Interfaces.Persistence;
using NexaStore.Persistence.DatabaseContext;

namespace NexaStore.Persistence;

public class UnitOfWork : IUnitOfWork
{
    private readonly AppDbContext _context;

    // Holds the active explicit transaction if BeginTransactionAsync was called.
    // Null when using the default implicit-per-SaveChanges transaction.
    private IDbContextTransaction? _transaction;

    // INTERVIEW: UnitOfWork is registered as Scoped — same lifetime as the HTTP request.
    // One AppDbContext per request = one change tracker per request = one UoW per request.
    // This means all repository operations in a single request share the same
    // DbContext, so they all participate in the same implicit transaction.
    public UnitOfWork(AppDbContext context)
    {
        _context = context;
    }

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        // Delegates directly to AppDbContext.SaveChangesAsync which:
        // 1. Auto-sets CreatedAt / UpdatedAt on BaseEntity (audit interception)
        // 2. Persists all tracked changes in one DB transaction
        // 3. Dispatches domain events after successful save
        // All three happen here with one call from the handler.
        return await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        // INTERVIEW: BeginTransactionAsync creates an EXPLICIT database transaction.
        // Use this when you need fine-grained control over commit/rollback boundaries
        // that span multiple SaveChangesAsync calls.
        //
        // Example scenario:
        //   await _unitOfWork.BeginTransactionAsync();
        //   try {
        //     // Step 1 — save order
        //     await _orderRepository.AddAsync(order);
        //     await _unitOfWork.SaveChangesAsync();
        //     // Step 2 — call external service, then save result
        //     var result = await _externalService.DoSomething();
        //     await _unitOfWork.SaveChangesAsync();
        //     await _unitOfWork.CommitTransactionAsync();
        //   } catch {
        //     await _unitOfWork.RollbackTransactionAsync();
        //     throw;
        //   }
        _transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
    }

    public async Task CommitTransactionAsync(CancellationToken cancellationToken = default)
    {
        // Guard — cannot commit if no explicit transaction was started
        if (_transaction is null)
            throw new InvalidOperationException(
                "Cannot commit — no active transaction. Call BeginTransactionAsync first.");

        try
        {
            // Commit all changes made since BeginTransactionAsync
            await _transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            // If commit fails, roll back to leave DB in a consistent state
            await RollbackTransactionAsync(cancellationToken);
            throw;
        }
        finally
        {
            // Always dispose the transaction object after commit or rollback
            // INTERVIEW: Not disposing leaves a DB connection open indefinitely
            await _transaction.DisposeAsync();
            _transaction = null;
        }
    }

    public async Task RollbackTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction is null)
            throw new InvalidOperationException(
                "Cannot rollback — no active transaction. Call BeginTransactionAsync first.");

        // INTERVIEW: Rollback undoes all changes since BeginTransactionAsync.
        // Called in catch blocks when any step in a multi-step operation fails.
        await _transaction.RollbackAsync(cancellationToken);
    }

    public void Dispose()
    {
        // INTERVIEW: Dispose pattern — clean up the transaction if it was never
        // committed (e.g. exception path that forgot to call Rollback).
        // The DbContext itself is disposed by the DI container (it's Scoped),
        // so we only need to handle the explicit transaction here.
        _transaction?.Dispose();

        // INTERVIEW: We intentionally do NOT dispose _context here.
        // AppDbContext is registered as Scoped in DI — the container manages its
        // lifetime and will dispose it at the end of the request scope.
        // Disposing it here could cause "disposed context" exceptions if anything
        // else in the same request scope tries to use it after UoW is disposed.
    }
}
