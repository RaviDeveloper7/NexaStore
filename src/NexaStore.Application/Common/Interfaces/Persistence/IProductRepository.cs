// IProductRepository.cs — product-specific query contracts.
// INTERVIEW: Extends IGenericRepository<Product> to inherit all CRUD operations
// and adds queries that are specific to the Product domain — pagination,
// filtering by category, and stock-level checks that the generic repo can't express.

using NexaStore.Application.Common.Models;
using NexaStore.Domain.Entities;

namespace NexaStore.Application.Common.Interfaces.Persistence;

public interface IProductRepository : IGenericRepository<Product>
{
    // Paged, filtered, sorted product list — used by GetProductsQueryHandler
    // INTERVIEW: Pagination is done at the DB level (Skip/Take in EF) — never
    // load all rows into memory and filter in C#. That kills performance at scale.
    Task<PagedResult<Product>> GetPagedAsync(
        int pageNumber,
        int pageSize,
        string? searchTerm,          // Filter by name/description
        Guid? categoryId,            // Filter by category
        string? sortBy,              // Column to sort by
        bool isDescending,           // Sort direction
        CancellationToken cancellationToken = default);

    // Used by PlaceOrderCommandHandler to verify stock before creating an order
    // Returns products matching the given IDs in a single DB round-trip
    Task<IReadOnlyList<Product>> GetByIdsAsync(
        IEnumerable<Guid> ids,
        CancellationToken cancellationToken = default);
}
