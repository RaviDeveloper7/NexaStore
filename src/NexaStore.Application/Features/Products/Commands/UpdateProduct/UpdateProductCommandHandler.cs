using MediatR;
using NexaStore.Application.Common.Interfaces.Persistence;
using NexaStore.Application.Common.Interfaces.Services;
using NexaStore.Domain.Entities;
using NexaStore.Domain.Exceptions;

namespace NexaStore.Application.Features.Products.Commands.UpdateProduct;

public class UpdateProductCommandHandler
    : IRequestHandler<UpdateProductCommand, Unit>
{
    private readonly IProductRepository _productRepository;
    private readonly IGenericRepository<Category> _categoryRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICacheService _cacheService;

    public UpdateProductCommandHandler(
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

    public async Task<Unit> Handle(
        UpdateProductCommand command,
        CancellationToken cancellationToken)
    {
        // --- Load the existing product ---
        var product = await _productRepository
            .GetByIdAsync(command.Id, cancellationToken)
            ?? throw new NotFoundException(nameof(Product), command.Id);

        // --- Business rule: new Category must exist (if changing) ---
        // In: We only hit the DB for category validation if the category
        // is actually changing. If the client sends the same CategoryId,
        // we skip the lookup entirely — avoids an unnecessary DB round-trip.
        if (product.CategoryId != command.CategoryId)
        {
            var categoryExists = await _categoryRepository
                .ExistsAsync(c => c.Id == command.CategoryId, cancellationToken);

            if (!categoryExists)
                throw new NotFoundException(nameof(Category), command.CategoryId);
        }

        // --- Apply changes to the tracked entity ---
        // IN: Because GetByIdAsync (FindAsync) returns a TRACKED entity,
        // EF's change tracker detects these property assignments as modifications.
        // When SaveChangesAsync fires, EF generates:
        // UPDATE Products SET Name=@p1, Description=@p2, Price=@p3, ... WHERE Id=@p0
        // Only changed columns are included in the UPDATE statement.
        product.Name = command.Name;
        product.Description = command.Description;
        product.Price = command.Price;
        product.StockQuantity = command.StockQuantity;
        product.CategoryId = command.CategoryId;
        // UpdatedAt is set by AppDbContext.SaveChangesAsync audit interception

        // _productRepository.Update(product) is not needed here because EF already
        // tracks this entity from FindAsync. Update() would just re-mark it Modified.
        // IN: This is a subtle but important point — tracked entities auto-detect
        // changes. Update() is needed only for DETACHED entities (loaded with
        // AsNoTracking or from a different DbContext instance).

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // --- Invalidate cache ---
        // Bust the product list pages (new name/price changes search results)
        await _cacheService.RemoveByPrefixAsync("products:", cancellationToken);

        // Also bust the specific product detail cache
        // IN: The detail cache uses a per-product key: "product:{id}"
        // Without this, GET /products/{id} would serve stale data after an update.
        await _cacheService.RemoveAsync($"product:{command.Id}", cancellationToken);

        // IN: Unit.Value is MediatR's equivalent of returning void.
        // The framework requires a return value — Unit.Value is the convention.
        return Unit.Value;
    }
}
