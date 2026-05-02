// DeleteProductCommandHandler.cs — handles the DeleteProductCommand.
// IN: Delete has a critical business rule check that Create and Update don't:
// "Can you delete a product that has been ordered?"
// Answer: No. OrderItems reference Products via FK — deleting the product
// would orphan historical order data.
// The DB enforces this with DeleteBehavior.Restrict on the FK (OrderItemConfiguration).
// But we check it HERE in the handler to give a clean business error message
// BEFORE the DB throws a raw FK violation exception.
// IN: DB FK violation → SqlException with error code 547 (ugly, internal).
// Business rule check → NotFoundException or BadRequestException (clean, meaningful).
// Always prefer catching the problem at the application layer.

using MediatR;
using NexaStore.Application.Common.Interfaces.Persistence;
using NexaStore.Application.Common.Interfaces.Services;
using NexaStore.Domain.Entities;
using NexaStore.Domain.Exceptions;

namespace NexaStore.Application.Features.Products.Commands.DeleteProduct;

public class DeleteProductCommandHandler
    : IRequestHandler<DeleteProductCommand, Unit>
{
    private readonly IProductRepository _productRepository;
    private readonly IOrderRepository _orderRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICacheService _cacheService;

    public DeleteProductCommandHandler(
        IProductRepository _productRepository,
        IOrderRepository orderRepository,
        IUnitOfWork unitOfWork,
        ICacheService cacheService)
    {
        this._productRepository = _productRepository;
        _orderRepository = orderRepository;
        _unitOfWork = unitOfWork;
        _cacheService = cacheService;
    }

    public async Task<Unit> Handle(
        DeleteProductCommand command,
        CancellationToken cancellationToken)
    {
        // --- Load the product ---
        var product = await _productRepository
            .GetByIdAsync(command.Id, cancellationToken)
            ?? throw new NotFoundException(nameof(Product), command.Id);

        // --- Business rule: cannot delete a product that has been ordered ---
        // IN: We check if any OrderItem references this ProductId.
        // AnyAsync() translates to SELECT TOP 1 1 FROM OrderItems WHERE ProductId = @id
        // — the fastest possible existence check, no columns fetched.
        // If any order has ever included this product, we reject the delete.
        // The correct alternative for production: soft-delete (IsDeleted = true).
        // Soft-delete hides the product from the catalog without removing the FK reference,
        // preserving historical order data integrity permanently.
        var isOrdered = await _orderRepository.ExistsAsync(
            o => o.Items.Any(i => i.ProductId == command.Id),
            cancellationToken);

        if (isOrdered)
            throw new BadRequestException(
                $"Product '{product.Name}' cannot be deleted because it has existing orders. " +
                "Consider discontinuing the product instead.");

        // --- Delete and persist ---
        _productRepository.Delete(product);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // --- Invalidate cache ---
        await _cacheService.RemoveByPrefixAsync("products:", cancellationToken);
        await _cacheService.RemoveAsync($"product:{command.Id}", cancellationToken);

        return Unit.Value;
    }
}
