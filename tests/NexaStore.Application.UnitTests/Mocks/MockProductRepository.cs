// MockProductRepository.cs — a hand-built in-memory fake of IProductRepository.
// IN: Two approaches exist for test doubles — Moq (dynamic mock) or a hand-written
// fake backed by an in-memory List<T>. We use hand-written fakes for repositories
// because they behave like a REAL repository — GetByIdsTrackedAsync actually
// filters, AddAsync actually adds. Moq would require configuring every method's
// return value per test — tedious and brittle for a repository interface this large.
// We reserve Moq for simpler dependencies (ICacheService, ICurrentUserService)
// where a handful of Setup() calls per test is manageable.

using System.Linq.Expressions;
using NexaStore.Application.Common.Interfaces.Persistence;
using NexaStore.Application.Common.Models;
using NexaStore.Domain.Entities;

namespace NexaStore.Application.UnitTests.Mocks;

public class MockProductRepository : IProductRepository
{
    // IN: Public list — tests seed data directly via _repository.Products.Add(...)
    // before invoking the handler. This is the "test data builder" pattern.
    public List<Product> Products { get; } = new();

    public Task<IReadOnlyList<Product>> GetAllAsync(
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<Product>>(Products.ToList());

    public Task<Product?> GetByIdAsync(
        Guid id, CancellationToken cancellationToken = default)
        => Task.FromResult(Products.FirstOrDefault(p => p.Id == id));

    public Task<IReadOnlyList<Product>> GetAsync(
        Expression<Func<Product, bool>> predicate,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<Product>>(
            Products.AsQueryable().Where(predicate).ToList());

    public Task<bool> ExistsAsync(
        Expression<Func<Product, bool>> predicate,
        CancellationToken cancellationToken = default)
        => Task.FromResult(Products.AsQueryable().Any(predicate));

    public Task AddAsync(
        Product entity, CancellationToken cancellationToken = default)
    {
        Products.Add(entity);
        return Task.CompletedTask;
    }

    public void Update(Product entity)
    {
        // IN: In-memory list already holds a reference to the same object —
        // "updating" is a no-op since mutations are already reflected.
        // A real EF repository would mark it Modified; here there's nothing to do.
    }

    public void Delete(Product entity) => Products.Remove(entity);

    public Task<PagedResult<Product>> GetPagedAsync(
        int pageNumber, int pageSize, string? searchTerm, Guid? categoryId,
        string? sortBy, bool isDescending,
        CancellationToken cancellationToken = default)
    {
        var query = Products.AsQueryable();

        if (!string.IsNullOrWhiteSpace(searchTerm))
            query = query.Where(p => p.Name.Contains(searchTerm));

        if (categoryId.HasValue)
            query = query.Where(p => p.CategoryId == categoryId.Value);

        var totalCount = query.Count();

        var items = query
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return Task.FromResult(
            new PagedResult<Product>(items, totalCount, pageNumber, pageSize));
    }

    public Task<IReadOnlyList<Product>> GetByIdsAsync(
        IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
    {
        var idList = ids.ToList();
        return Task.FromResult<IReadOnlyList<Product>>(
            Products.Where(p => idList.Contains(p.Id)).ToList());
    }

    // IN: GetByIdsTrackedAsync — the method PlaceOrderCommandHandler actually calls.
    // In the fake, "tracked" and "untracked" are identical — the in-memory
    // list holds live references, so mutating a returned Product IS the
    // same object as the one in Products. Perfect for testing stock decrement:
    // the test can assert Products.First(p => p.Id == x).StockQuantity after
    // Handle() runs, exactly like a real tracked entity would behave.
    public Task<IReadOnlyList<Product>> GetByIdsTrackedAsync(
        IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
        => GetByIdsAsync(ids, cancellationToken);

    public Task<Product?> GetByIdWithCategoryAsync(
        Guid id, CancellationToken cancellationToken = default)
        => Task.FromResult(Products.FirstOrDefault(p => p.Id == id));
}
