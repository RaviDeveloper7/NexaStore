// PlaceOrderCommandValidator.cs — validates the structure of PlaceOrderCommand.

using FluentValidation;

namespace NexaStore.Application.Features.Orders.Commands.PlaceOrder;

public class PlaceOrderCommandValidator : AbstractValidator<PlaceOrderCommand>
{
    public PlaceOrderCommandValidator()
    {
        // --- Items collection ---
        RuleFor(x => x.Items)
            .NotEmpty()
                .WithMessage("An order must contain at least one item.")
            .Must(items => items.Count <= 50)
                .WithMessage("An order cannot contain more than 50 distinct items.");
        // IN: Max 50 items per order is a business policy guard.
        // Without it, a malicious client could send 10,000 items in one request,
        // causing GetByIdsAsync to issue a massive IN clause and hammering the DB.
        // Validate the collection size BEFORE hitting the database.

        // --- Each item in the collection ---
        // IN: RuleForEach applies the same ruleset to every element
        // in the Items collection. Each ChildRules block scopes rules to
        // one OrderItemRequest. Errors are reported per-item with index:
        // "Items[0].ProductId: Product is required."
        RuleForEach(x => x.Items)
            .ChildRules(item =>
            {
                item.RuleFor(i => i.ProductId)
                    .NotEmpty()
                        .WithMessage("Product is required.");

                item.RuleFor(i => i.Quantity)
                    .GreaterThan(0)
                        .WithMessage("Quantity must be at least 1.")
                    .LessThanOrEqualTo(1000)
                        .WithMessage("Quantity per item cannot exceed 1,000.");
                // IN: Upper bound on quantity prevents absurd orders
                // that would decimate stock in one request. This is input sanity,
                // not a real business cap — adjust to domain requirements.
            });

        // --- No duplicate ProductIds ---
        // IN: If a customer sends ProductId X twice in one order,
        // the handler would decrement stock twice and create two OrderItems
        // for the same product — an order data integrity problem.
        // Catch it here before the handler fires.
        // Must() takes a predicate — if it returns false, the rule fails.
        RuleFor(x => x.Items)
            .Must(items => items
                .Select(i => i.ProductId)
                .Distinct()
                .Count() == items.Count)
            .WithMessage("Duplicate products found. Each product must appear only once per order.")
            .When(x => x.Items is { Count: > 0 });
    }
}
