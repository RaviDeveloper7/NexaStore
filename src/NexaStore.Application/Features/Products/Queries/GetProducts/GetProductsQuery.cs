
// In: Why does GetProductsQuery extend PaginationParams instead of
// containing it as a property?
// Inheritance flattens the properties so the controller can bind all of them
// directly from query string parameters without nesting:
//   GET /products?pageNumber=1&pageSize=10&searchTerm=phone&sortBy=price
// If PaginationParams were a property, the client would need:
//   GET /products?pagination.pageNumber=1&pagination.pageSize=10 (ugly, non-standard)
// Inheritance gives clean flat query strings — standard REST convention.

using MediatR;
using NexaStore.Application.Common.Models;

namespace NexaStore.Application.Features.Products.Queries.GetProducts;

public class GetProductsQuery : PaginationParams, IRequest<PagedResult<ProductListDto>>
{
    // CategoryId filter — optional. Null means "all categories".
    // Set by the client when browsing a specific category page.
    public Guid? CategoryId { get; set; }

}
