// CreateProductCommandHandler.cs — handles the CreateProductCommand.
// The handler is the ONLY place business logic executes for this operation.
// It has exactly these responsibilities:
// 1. Validate the category exists (business rule — not validation layer concern)
// 2. Create the Product entity
// 3. Persist via repository + UnitOfWork
// 4. Invalidate the product list cache (Redis) — stale cache after a new product
// Nothing else. No HTTP, no EF, no SQL — pure application orchestration.

using NexaStore.Application.Common.Interfaces.Persistence;
using NexaStore.Application.Common.Interfaces.Services;
using NexaStore.Domain.Entities;
using NexaStore.Domain.Exceptions;
using MediatR;

namespace NexaStore.Application.Features.Products.Commands.CreateProduct;

public class CreateProductCommandHandler
    : IRequestHandler<CreateProductCommand, Guid>
{
    private readonly IProductRepository _productRepository;
    private readonly IGenericRepository<Category> _categoryRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICacheService _cacheService;

    public CreateProductCommandHandler(
        IProductRepository productRepository,
        IGenericRepository<Category> categoryRepository,
        IUnitOfWork unitOfWork,
        ICacheService cacheService)
    {
        _productRepository = productRepository;
        _categoryRepository = categoryRepository;
        _unitOfWork = unitOfWork;
        _cacheService = cacheService;
    }

    public async Task<Guid> Handle(
        CreateProductCommand command,
        CancellationToken cancellationToken)
    {
        // --- Business rule: Category must exist ---
        // In: This is a BUSINESS RULE check, not an input validation check.
        // Input validation (FluentValidation) checks: "is CategoryId a non-empty Guid?"
        // Business rule (here) checks: "does this CategoryId actually exist in the DB?"
        // The distinction matters — business rules belong in handlers, not validators.
        // Validators run before the DB is touched. Business rules require a DB lookup.
        var categoryExists = await _categoryRepository
            .ExistsAsync(c => c.Id == command.CategoryId, cancellationToken);

        if (!categoryExists)
            throw new NotFoundException(nameof(Category), command.CategoryId);

        // --- Build the domain entity ---
        // IN: The handler builds the entity — not the controller, not the validator.
        // The entity is a pure domain object. We set properties here because this
        // is the application's "construction site" for new entities.
        // Guid.NewGuid() here — app-generated Id, not DB-generated.
        var product = new Product
        {
            Id = Guid.NewGuid(),
            Name = command.Name,
            Description = command.Description,
            Price = command.Price,
            StockQuantity = command.StockQuantity,
            CategoryId = command.CategoryId,
            // CreatedAt is set by AppDbContext.SaveChangesAsync audit interception
            // — we do not set it here. Single source of truth for audit timestamps.
        };

        // --- Persist ---
        // AddAsync enrols the entity in EF's change tracker (no DB call yet)
        await _productRepository.AddAsync(product, cancellationToken);

        // SaveChangesAsync commits the INSERT in one DB round-trip.
        // IN: The UnitOfWork pattern means all changes since the last
        // SaveChangesAsync are committed atomically. If we added more entities
        // before this call, they'd all go in the same transaction.
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // --- Invalidate cache ---
        // IN: After a new product is created, all cached product list pages
        // are stale — they don't include the new product.
        // RemoveByPrefixAsync("products:") busts ALL paginated cache entries at once.
        // Key pattern: "products:page=1:size=10:search=:category=:sort=name:desc=false"
        // Every variant that starts with "products:" is invalidated.
        // Trade-off: this is more aggressive than needed (only page 1 might be stale)
        // but simpler and safer than trying to invalidate specific pages.
        await _cacheService.RemoveByPrefixAsync("products:", cancellationToken);

        // Return the new product's Id — client uses this to fetch the full product
        return product.Id;
    }
}
