// GetProductsQueryHandler.cs — the most feature-rich query handler in the system.
// Implements: Pagination + Filtering + Sorting + Redis Caching + Mapster mapping.
//
// IN: Cache-Aside Pattern (also called Lazy Loading Cache):
// 1. Check Redis for the cached result
// 2. If hit  → return cached data immediately (zero DB call)
// 3. If miss → query the DB, map result, store in Redis, return result
//
// This pattern is correct for read-heavy, write-infrequent data like product catalogs.
// Products are created/updated rarely compared to how often they are listed.
// Cache TTL of 5 minutes means a product update appears to clients within 5 minutes
// even if the cache isn't explicitly busted (belt-and-suspenders approach).
//
// IN: Why cache the DTO (ProductListDto) and not the domain entity (Product)?
// Caching entities means storing EF navigation properties, change tracker state,
// and potentially circular references — all of which break JSON serialization.
// DTOs are plain data — serialize perfectly to JSON. Cache DTOs always.

using MapsterMapper;
using MediatR;
using NexaStore.Application.Common.Interfaces.Persistence;
using NexaStore.Application.Common.Interfaces.Services;
using NexaStore.Application.Common.Models;

namespace NexaStore.Application.Features.Products.Queries.GetProducts;

public class GetProductsQueryHandler
    : IRequestHandler<GetProductsQuery, PagedResult<ProductListDto>>
{
    private readonly IProductRepository _productRepository;
    private readonly ICacheService _cacheService;
    private readonly IMapper _mapper;

    // IN: Three dependencies — repository (DB), cache (Redis), mapper (Mapster).
    // Handler coordinates them — it doesn't implement any of them.
    // This is pure orchestration — the "Application" layer's only job.
    public GetProductsQueryHandler(
        IProductRepository productRepository,
        ICacheService cacheService,
        IMapper mapper)
    {
        _productRepository = productRepository;
        _cacheService = cacheService;
        _mapper = mapper;
    }

    public async Task<PagedResult<ProductListDto>> Handle(
        GetProductsQuery query,
        CancellationToken cancellationToken)
    {
        // --- Build deterministic cache key ---
        // IN: The cache key must uniquely identify this EXACT query —
        // same parameters = same key = same cached result.
        // Every parameter that affects the result must be in the key.
        // Forgetting one (e.g. omitting SortBy) means:
        //   GET /products?sortBy=price returns the same cache as GET /products?sortBy=name
        //   → wrong sorted results served from cache — a subtle, hard-to-debug bug.
        //
        // Format: "products:p={page}:s={size}:q={search}:cat={category}:sort={col}:desc={dir}"
        // Using short abbreviations keeps keys readable in Redis CLI / Azure Portal.
        var cacheKey = $"products:" +
                       $"p={query.PageNumber}:" +
                       $"s={query.PageSize}:" +
                       $"q={query.SearchTerm ?? string.Empty}:" +
                       $"cat={query.CategoryId?.ToString() ?? string.Empty}:" +
                       $"sort={query.SortBy ?? string.Empty}:" +
                       $"desc={query.IsDescending}";

        // --- Step 1: Check Redis cache (Cache-Aside Pattern) ---
        var cached = await _cacheService
            .GetAsync<PagedResult<ProductListDto>>(cacheKey, cancellationToken);

        if (cached is not null)
        {
            // IN: Cache HIT — return immediately.
            // Zero DB calls. Redis response time is ~1ms (in-process network).
            // SQL Server query for a paged product list: ~10-50ms.
            // At scale, caching eliminates 95%+ of DB load for product list queries.
            return cached;
        }

        // --- Step 2: Cache MISS — query the database ---
        // IN: GetPagedAsync lives in IProductRepository (not IGenericRepository)
        // because pagination + filtering + sorting is product-specific logic.
        // The generic repository has no concept of "search by name" or "sort by price".
        var pagedProducts = await _productRepository.GetPagedAsync(
            pageNumber: query.PageNumber,
            pageSize: query.PageSize,
            searchTerm: query.SearchTerm,
            categoryId: query.CategoryId,
            sortBy: query.SortBy,
            isDescending: query.IsDescending,
            cancellationToken);

        // --- Step 3: Map domain entities to DTOs ---
        // IN: Map BEFORE caching — cache the DTO, not the entity.
        // Entity has EF navigation properties that don't serialize cleanly.
        // ProductListDto is a plain record — serializes perfectly to JSON.
        // Mapster uses the ProductMappingProfile registered in Week 2 Day 5:
        // CategoryName is mapped from product.Category.Name.
        // This works because GetPagedAsync uses .Include(p => p.Category).
        var dtos = _mapper.Map<List<ProductListDto>>(pagedProducts.Items);

        // Reconstruct PagedResult with DTOs instead of entities
        // IN: We can't just map PagedResult<Product> → PagedResult<ProductListDto>
        // directly because PagedResult is a generic wrapper, not a Mapster-registered type.
        // Re-construct it manually — straightforward and explicit.
        var result = new PagedResult<ProductListDto>(
            dtos,
            pagedProducts.TotalCount,
            pagedProducts.PageNumber,
            pagedProducts.PageSize);

        // --- Step 4: Store result in Redis ---
        // IN: TTL of 5 minutes — the cache is authoritative for 5 minutes.
        // After TTL expires, the next request repopulates the cache from DB.
        // This is the "belt" in belt-and-suspenders — even if cache invalidation
        // in CreateProduct/UpdateProduct/DeleteProduct handlers fails (Redis down,
        // bug in the code), the data self-corrects within 5 minutes.
        // TTL choice: short enough that stale data isn't a real problem,
        // long enough to absorb a spike of concurrent product list requests.
        await _cacheService.SetAsync(
            cacheKey,
            result,
            expiry: TimeSpan.FromMinutes(5),
            cancellationToken);

        return result;
    }
}
