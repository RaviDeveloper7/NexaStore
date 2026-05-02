using FluentValidation;

namespace NexaStore.Application.Features.Products.Commands.UpdateProduct;

public class UpdateProductCommandValidator
    : AbstractValidator<UpdateProductCommand>
{
    public UpdateProductCommandValidator()
    {
        // --- Id ---
        RuleFor(x => x.Id)
            .NotEmpty()
                .WithMessage("Product Id is required.");

        // --- Name ---
        RuleFor(x => x.Name)
            .NotEmpty()
                .WithMessage("Product name is required.")
            .MaximumLength(200)
                .WithMessage("Product name must not exceed 200 characters.")
            .MinimumLength(2)
                .WithMessage("Product name must be at least 2 characters.");

        // --- Description ---
        RuleFor(x => x.Description)
            .MaximumLength(2000)
                .WithMessage("Description must not exceed 2000 characters.")
            .When(x => x.Description is not null);

        // --- Price ---
        RuleFor(x => x.Price)
            .GreaterThan(0)
                .WithMessage("Price must be greater than zero.")
            .LessThanOrEqualTo(999999.99m)
                .WithMessage("Price must not exceed 999,999.99.");

        // --- StockQuantity ---
        RuleFor(x => x.StockQuantity)
            .GreaterThanOrEqualTo(0)
                .WithMessage("Stock quantity cannot be negative.");

        // --- CategoryId ---
        RuleFor(x => x.CategoryId)
            .NotEmpty()
                .WithMessage("Category is required.");
    }
}
