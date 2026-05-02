// CreateProductCommandValidator.cs — FluentValidation rules for CreateProductCommand.

using FluentValidation;

namespace NexaStore.Application.Features.Products.Commands.CreateProduct;

public class CreateProductCommandValidator
    : AbstractValidator<CreateProductCommand>
{
    public CreateProductCommandValidator()
    {
        // --- Name ---
        RuleFor(x => x.Name)
            .NotEmpty()
                .WithMessage("Product name is required.")
            .MaximumLength(200)
                .WithMessage("Product name must not exceed 200 characters.")
            .MinimumLength(2)
                .WithMessage("Product name must be at least 2 characters.");

        // --- Description ---
        // Optional — but if provided, enforce a max length
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
        // Zero stock is valid — a product can be listed before stock arrives
        RuleFor(x => x.StockQuantity)
            .GreaterThanOrEqualTo(0)
                .WithMessage("Stock quantity cannot be negative.");

        // --- CategoryId ---
        RuleFor(x => x.CategoryId)
            .NotEmpty()
                .WithMessage("Category is required.");
    }
}
