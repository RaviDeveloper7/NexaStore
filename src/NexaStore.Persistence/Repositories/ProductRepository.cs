// ProductRepository.cs — product-specific query implementations.
// INTERVIEW: Inherits all CRUD from GenericRepository<Product>.
// This class only adds what the generic repo cannot express:
// 1. Paginated + filtered + sorted product listing with Redis-cacheable results
// 2. Batch fetch by IDs for stock validation in PlaceOrderCommandHandler

using Microsoft.EntityFrameworkCore;
using NexaStore.Application.Common.Interfaces.Persistence;
using NexaStore.Application.Common.Models;
using NexaStore.Domain.Entities;
using NexaStore.Persistence.DatabaseContext;

namespace NexaStore.Persistence.Repositories;

public class ProductRepository : GenericRepository<Product>, IProductRepository
{
    public ProductRepository(AppDbContext context) : base(context)
    {
    }

    public async Task<PagedResult<Product>> GetPagedAsync(
        int pageNumber,
        int pageSize,
        string? searchTerm,
        Guid? categoryId,
        string? sortBy,
        bool isDescending,
        CancellationToken cancellationToken = default)
    {
        // INTERVIEW: IQueryable<T> builds a query expression tree — nothing hits
        // the DB until ToListAsync() or CountAsync() is called.
        // Every .Where() and .OrderBy() call appends to the SQL, not to in-memory data.
        // This is fundamentally different from IEnumerable<T> which would load all rows first.
        var query = _dbSet
            .AsNoTracking()
            .Include(p => p.Category)  // Eager load Category — needed for ProductListDto
            .AsQueryable();

        // --- Filtering ---

        // Search: case-insensitive LIKE on Name and Description
        // INTERVIEW: EF Core translates .Contains() to SQL LIKE '%term%'.
        // For production search you'd use Full-Text Search or Azure Cognitive Search,
        // but LIKE is correct for a portfolio project.
        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            var term = searchTerm.Trim().ToLower();
            query = query.Where(p =>
                p.Name.ToLower().Contains(term) ||
                (p.Description != null && p.Description.ToLower().Contains(term)));
        }

        // Category filter — exact match on CategoryId FK
        if (categoryId.HasValue)
        {
            query = query.Where(p => p.CategoryId == categoryId.Value);
        }

        // --- Count BEFORE pagination ---
        // INTERVIEW: Get total count from the filtered (but not yet paged) query.
        // This is the correct count for pagination metadata.
        // Never count after Skip/Take — that would always return pageSize or less.
        var totalCount = await query.CountAsync(cancellationToken);

        // --- Sorting ---
        // INTERVIEW: Dynamic sorting without raw SQL — switch on the column name,
        // apply OrderBy/OrderByDescending. This is type-safe and injection-proof.
        // Raw SQL ORDER BY with string interpolation would be a SQL injection risk.
        query = sortBy?.ToLower() switch
        {
            "name" => isDescending
                                ? query.OrderByDescending(p => p.Name)
                                : query.OrderBy(p => p.Name),

            "price" => isDescending
                                ? query.OrderByDescending(p => p.Price)
                                : query.OrderBy(p => p.Price),

            "stockquantity" => isDescending
                                ? query.OrderByDescending(p => p.StockQuantity)
                                : query.OrderBy(p => p.StockQuantity),

            "createdat" => isDescending
                                ? query.OrderByDescending(p => p.CreatedAt)
                                : query.OrderBy(p => p.CreatedAt),

            // Default sort: newest first — most relevant for an e-commerce catalog
            _ => query.OrderByDescending(p => p.CreatedAt)
        };

        // --- Pagination ---
        // INTERVIEW: Skip/Take translates to SQL OFFSET/FETCH NEXT.
        // This means the DB only returns pageSize rows — not all rows.
        // (pageNumber - 1) because pageNumber is 1-based, Skip is 0-based.
        var items = await query
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        // Wrap results in PagedResult with metadata for the client
        return new PagedResult<Product>(items, totalCount, pageNumber, pageSize);
    }

    public async Task<IReadOnlyList<Product>> GetByIdsAsync(
        IEnumerable<Guid> ids,
        CancellationToken cancellationToken = default)
    {
        // INTERVIEW: One DB round-trip for all product IDs in the order.
        // The alternative — loop and call GetByIdAsync per product — is an N+1 query.
        // For an order with 10 items, N+1 fires 10 SELECTs vs this fires 1.
        // EF translates .Contains() on a collection to SQL IN (...).
        var idList = ids.ToList(); // Materialise to avoid multiple enumeration

        return await _dbSet
            .AsNoTracking()
            .Where(p => idList.Contains(p.Id))
            .ToListAsync(cancellationToken);
    }
}
