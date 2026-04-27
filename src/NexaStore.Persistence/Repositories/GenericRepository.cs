// GenericRepository.cs — base implementation of IGenericRepository<T>.
// INTERVIEW: The Generic Repository pattern gives you CRUD for free on every entity.
// No copy-pasting Add/Update/Delete across 10 repository classes.
// Specific repos (ProductRepository, OrderRepository) inherit this and
// add only the domain-specific queries they need.
//
// Key decision: repository methods do NOT call SaveChangesAsync.
// Saving is ALWAYS done through IUnitOfWork.SaveChangesAsync().
// This is what makes the Unit of Work pattern work — multiple repository
// operations commit together in one transaction.

using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using NexaStore.Application.Common.Interfaces.Persistence;
using NexaStore.Domain.Entities;
using NexaStore.Persistence.DatabaseContext;

namespace NexaStore.Persistence.Repositories;

public class GenericRepository<T> : IGenericRepository<T> where T : BaseEntity
{
    // Protected so derived repositories can access DbContext and DbSet directly
    // without re-injecting them
    protected readonly AppDbContext _context;
    protected readonly DbSet<T> _dbSet;

    public GenericRepository(AppDbContext context)
    {
        _context = context;

        // INTERVIEW: _dbSet is cached here rather than calling _context.Set<T>()
        // on every method. Minor performance gain, but also cleaner code in
        // derived classes that need direct DbSet access.
        _dbSet = context.Set<T>();
    }

    // --- Queries ---

    public async Task<IReadOnlyList<T>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        // INTERVIEW: AsNoTracking() — returns entities without EF change tracking.
        // For read-only queries this is always faster because EF skips the
        // overhead of building the identity map and snapshot for change detection.
        // Rule: use AsNoTracking() for any query where you won't call Update() on the result.
        return await _dbSet
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task<T?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        // INTERVIEW: FindAsync checks the EF change tracker first before hitting the DB.
        // If the entity was already loaded in this request's DbContext lifetime,
        // FindAsync returns the cached instance with zero DB round-trips.
        // This is different from FirstOrDefaultAsync which always hits the DB.
        return await _dbSet.FindAsync(new object[] { id }, cancellationToken);
    }

    public async Task<IReadOnlyList<T>> GetAsync(
        Expression<Func<T, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        // INTERVIEW: Expression<Func<T, bool>> is key — EF Core translates this
        // LINQ expression into a SQL WHERE clause at the DB level.
        // A plain Func<T, bool> would load ALL rows into memory first then filter —
        // that's catastrophic at scale. Always use Expression<> for DB queries.
        return await _dbSet
            .AsNoTracking()
            .Where(predicate)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> ExistsAsync(
        Expression<Func<T, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        // INTERVIEW: AnyAsync translates to SELECT TOP 1 1 FROM ... WHERE ...
        // It's the fastest existence check — no columns fetched, no entity
        // constructed. Never do GetByIdAsync() != null for existence checks.
        return await _dbSet.AnyAsync(predicate, cancellationToken);
    }

    // --- Commands ---

    public async Task AddAsync(
        T entity,
        CancellationToken cancellationToken = default)
    {
        // Adds entity to EF change tracker in Added state.
        // INTERVIEW: This does NOT hit the database. The INSERT happens when
        // IUnitOfWork.SaveChangesAsync() is called. This is intentional —
        // multiple AddAsync calls can be batched into one DB round-trip.
        await _dbSet.AddAsync(entity, cancellationToken);
    }

    public void Update(T entity)
    {
        // INTERVIEW: Update() marks the entire entity as Modified.
        // EF will generate an UPDATE for ALL columns, not just changed ones.
        // More granular: attach entity and set specific properties as modified.
        // For this project, full-entity updates are acceptable and simpler.
        _dbSet.Update(entity);
    }

    public void Delete(T entity)
    {
        // Marks entity as Deleted in the change tracker.
        // INTERVIEW: We implement hard delete here. A real production system
        // often uses soft delete (IsDeleted flag) instead.
        // Soft delete would be implemented via a global query filter:
        // modelBuilder.Entity<T>().HasQueryFilter(e => !e.IsDeleted)
        // which automatically appends WHERE IsDeleted = 0 to every query.
        _dbSet.Remove(entity);
    }
}
