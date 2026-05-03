// GetProductByIdQuery.cs — fetches a single product by its Id.
// INTERVIEW: Returns ProductDetailDto — the full product representation
// including Description, CategoryId, UpdatedAt.
// ProductListDto (used in GetProductsQuery) deliberately omits these
// to keep the list payload lean. Detail view needs everything.

using MediatR;
using NexaStore.Application.Features.Products.Queries.GetProductById;

namespace NexaStore.Application.Features.Products.Queries.GetProductById;

public class GetProductByIdQuery : IRequest<ProductDetailDto>
{
    public Guid Id { get; set; }

    // Constructor for convenient instantiation from the controller
    // mediator.Send(new GetProductByIdQuery(id))
    public GetProductByIdQuery(Guid id) => Id = id;
}
