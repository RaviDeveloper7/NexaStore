// ProductDetailDto.cs — full product representation for detail/edit views.
// Carries everything including Description and UpdatedAt.

namespace NexaStore.Application.Features.Products.Queries.GetProductById;

public class ProductDetailDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public int StockQuantity { get; set; }
    public bool IsInStock => StockQuantity > 0;

    // Category info embedded — avoids a second API call from the client
    public Guid CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
