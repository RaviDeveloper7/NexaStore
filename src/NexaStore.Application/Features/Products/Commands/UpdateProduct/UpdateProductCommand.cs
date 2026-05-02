using MediatR;

namespace NexaStore.Application.Features.Products.Commands.UpdateProduct;

public class UpdateProductCommand : IRequest<Unit>
{
   
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public int StockQuantity { get; set; }
    public Guid CategoryId { get; set; }
}
