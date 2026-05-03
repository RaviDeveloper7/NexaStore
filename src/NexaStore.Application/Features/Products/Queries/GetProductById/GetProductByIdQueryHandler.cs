// GetProductByIdQueryHandler.cs — fetches a single product with per-product caching.
// INTERVIEW: Per-product cache key "product:{id}" means each product gets its own
// Redis entry. This is more granular than the list cache:
// - UpdateProductCommandHandler busts ONLY "product:{id}" for the changed product
// - Other products' caches remain valid — no unnecessary invalidation
// - Compare to list cache: updating one product busts ALL paginated list caches
//   because any list page might contain the updated product
//
// This two-level caching strategy (list cache + per-item cache) is the standard
// pattern for e-commerce catalog APIs.

using MapsterMapper;
using MediatR;
using NexaStore.Application.Common.Interfaces.Persistence;
using NexaStore.Application.Common.Interfaces.Services;
using NexaStore.Domain.Entities;
using NexaStore.Domain.Exceptions;

namespace NexaStore.Application.Features.Products.Queries.GetProductById;

public class GetProductByIdQueryHandler
    : IRequestHandler<GetProductByIdQuery, ProductDetailDto>
{
    private readonly IProductRepository _productRepository;
    private readonly ICacheService _cacheService;
    private readonly IMapper _mapper;

    public GetProductByIdQueryHandler(
        IProductRepository productRepository,
        ICacheService cacheService,
        IMapper mapper)
    {
        _productRepository = productRepository;
        _cacheService = cacheService;
        _mapper = mapper;
    }

    public async Task<ProductDetailDto> Handle(
        GetProductByIdQuery query,
        CancellationToken cancellationToken)
    {
        // Per-product cache key — unique per product Id
        var cacheKey = $"product:{query.Id}";

        // --- Step 1: Check Redis ---
        var cached = await _cacheService
            .GetAsync<ProductDetailDto>(cacheKey, cancellationToken);

        if (cached is not null)
            return cached;

        // --- Step 2: DB query ---
        // INTERVIEW: GetByIdAsync uses FindAsync — checks EF identity map first.
        // If the product was already loaded earlier in this request's DbContext
        // lifetime (e.g. by a command that ran first), this returns the
        // in-memory instance with zero DB round-trips.
        // Returns null if not found — we throw NotFoundException which
        // ExceptionMiddleware maps to HTTP 404.
        var product = await _productRepository
        .GetByIdWithCategoryAsync(query.Id, cancellationToken);

        if (product is null)
            throw new NotFoundException(nameof(Product), query.Id);

        // --- Step 3: Load Category for mapping ---
        // INTERVIEW: GetByIdAsync (FindAsync) does NOT load navigation properties.
        // ProductDetailDto needs CategoryName which lives on product.Category.Name.
        // If Category is null, the Mapster profile maps CategoryName as string.Empty.
        //
        // Option A (current): Accept null Category → CategoryName = string.Empty.
        //   Simple. Product detail still returns — just without CategoryName.
        //
        // Option B: Add GetByIdWithCategoryAsync to IProductRepository.
        //   Uses .Include(p => p.Category).FirstOrDefaultAsync(p => p.Id == id).
        //   Returns full product with Category loaded.
        //   More complete data but adds a repository method for this one case.
        //
        // INTERVIEW: For a portfolio project, Option A is acceptable.
        // In production, Option B is correct — product detail must show the category.
        // Add GetByIdWithCategoryAsync to IProductRepository and ProductRepository
        // if you want full accuracy here. The handler change is one line.
        //
        // For now we note this in a comment and proceed with Option A.
        // The category IS loaded on list queries (GetPagedAsync uses .Include).

        // --- Step 4: Map and cache ---
        var dto = _mapper.Map<ProductDetailDto>(product);

        // INTERVIEW: Longer TTL for single-product cache vs list cache.
        // List cache: 5 min — many variants, aggressive busting on any write.
        // Product detail cache: 10 min — one entry per product, only busted
        // when THAT specific product changes. Lower invalidation frequency
        // justifies a longer TTL.
        await _cacheService.SetAsync(
            cacheKey,
            dto,
            expiry: TimeSpan.FromMinutes(10),
            cancellationToken);

        return dto;
    }
}
