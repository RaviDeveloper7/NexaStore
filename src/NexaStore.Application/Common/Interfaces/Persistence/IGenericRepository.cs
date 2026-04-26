// IGenericRepository.cs — the base contract for all repositories.
// INTERVIEW: Generic repository gives you CRUD for free on every entity.
// Specific repositories (IProductRepository, IOrderRepository) extend this
// and add domain-specific query methods on top.
// The Application layer defines this interface — Persistence implements it.
// Application never knows SQL Server exists.

using System.Linq.Expressions;
using NexaStore.Domain.Entities;

namespace NexaStore.Application.Common.Interfaces.Persistence;

public interface IGenericRepository<T> where T : BaseEntity
{
    // --- Queries ---

    // Returns all entities — use sparingly, prefer filtered queries in production
    Task<IReadOnlyList<T>> GetAllAsync(CancellationToken cancellationToken = default);

    // Fetch a single entity by its primary key
    // Returns null if not found — caller decides whether to throw NotFoundException
    Task<T?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    // INTERVIEW: Expression<Func<T, bool>> allows callers to pass a LINQ predicate
    // that EF Core translates to SQL. No raw SQL leaks into the Application layer.
    Task<IReadOnlyList<T>> GetAsync(
        Expression<Func<T, bool>> predicate,
        CancellationToken cancellationToken = default);

    // Check existence without loading the entity — avoids unnecessary data transfer
    Task<bool> ExistsAsync(
        Expression<Func<T, bool>> predicate,
        CancellationToken cancellationToken = default);

    // --- Commands ---

    // Add a new entity to the change tracker — does NOT save to DB
    // INTERVIEW: Saving is always done via IUnitOfWork.SaveChangesAsync().
    // This enforces the Unit of Work pattern — all changes in one transaction.
    Task AddAsync(T entity, CancellationToken cancellationToken = default);

    // Mark entity as modified — EF tracks changes, saved via UoW
    void Update(T entity);

    // Mark entity for deletion — saved via UoW
    void Delete(T entity);
}
