// MockGenericRepository.cs — generic in-memory fake for any IGenericRepository<T>.
// IN: Because CreateProductCommandHandler depends on IGenericRepository<Category>
// (not a specific ICategoryRepository — categories have no domain-specific queries),
// one generic fake covers Category, Payment, or any other simple entity used
// directly through the generic interface across the test suite.

using System.Linq.Expressions;
using NexaStore.Application.Common.Interfaces.Persistence;
using NexaStore.Domain.Entities;

namespace NexaStore.Application.UnitTests.Mocks;

public class MockGenericRepository<T> : IGenericRepository<T> where T : BaseEntity
{
    public List<T> Items { get; } = new();

    public Task<IReadOnlyList<T>> GetAllAsync(
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<T>>(Items.ToList());

    public Task<T?> GetByIdAsync(
        Guid id, CancellationToken cancellationToken = default)
        => Task.FromResult(Items.FirstOrDefault(i => i.Id == id));

    public Task<IReadOnlyList<T>> GetAsync(
        Expression<Func<T, bool>> predicate,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<T>>(
            Items.AsQueryable().Where(predicate).ToList());

    public Task<bool> ExistsAsync(
        Expression<Func<T, bool>> predicate,
        CancellationToken cancellationToken = default)
        => Task.FromResult(Items.AsQueryable().Any(predicate));

    public Task AddAsync(T entity, CancellationToken cancellationToken = default)
    {
        Items.Add(entity);
        return Task.CompletedTask;
    }

    public void Update(T entity) { /* no-op — in-memory reference already updated */ }

    public void Delete(T entity) => Items.Remove(entity);
}
