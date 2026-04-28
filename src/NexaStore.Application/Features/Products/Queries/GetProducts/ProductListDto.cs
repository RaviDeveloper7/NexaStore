// ProductListDto.cs — lightweight product representation for list views.
// INTERVIEW: We have two product DTOs intentionally — ListDto and DetailDto.
// ListDto carries only what a grid/card view needs: name, price, stock, category.
// DetailDto carries everything including full description.
// Fetching only what you need reduces payload size and DB projection cost.
// Sending a 2000-char Description field for every row in a 50-item list is wasteful.

namespace NexaStore.Application.Features.Products.Queries.GetProducts;

public class ProductListDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int StockQuantity { get; set; }
    public string CategoryName { get; set; } = string.Empty;

    // INTERVIEW: IsInStock is a computed property on the DTO — not on the domain entity.
    // Domain entities should not carry UI/presentation concerns.
    // The handler maps StockQuantity, and the DTO exposes a boolean convenience flag.
    public bool IsInStock => StockQuantity > 0;
    public DateTime CreatedAt { get; set; }
}
