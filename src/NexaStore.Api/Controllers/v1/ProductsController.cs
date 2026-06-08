using Asp.Versioning;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexaStore.Application.Common.Models;
using NexaStore.Application.Features.Products.Commands.CreateProduct;
using NexaStore.Application.Features.Products.Commands.DeleteProduct;
using NexaStore.Application.Features.Products.Commands.UpdateProduct;
using NexaStore.Application.Features.Products.Queries.GetProductById;
using NexaStore.Application.Features.Products.Queries.GetProducts;
using NexaStore.Identity.Settings;

namespace NexaStore.Api.Controllers.v1;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/products")]
public class ProductsController : ControllerBase
{
    private readonly IMediator _mediator;

    public ProductsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>Get paged, filtered, and sorted product list. Results are Redis-cached.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<ProductListDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetProducts([FromQuery] GetProductsQuery query,CancellationToken cancellationToken)
    {
        // IN: [FromQuery] binds all GetProductsQuery properties from the query string.
        // GET /api/v1/products?pageNumber=1&pageSize=10&searchTerm=phone&sortBy=price
        // Because GetProductsQuery extends PaginationParams, all pagination params
        // bind automatically — no manual mapping needed.
        var result = await _mediator.Send(query, cancellationToken);
        return Ok(result);
    }

    /// <summary>Get a single product by Id. Result is Redis-cached.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ProductDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetProductById(Guid id,CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new GetProductByIdQuery(id), cancellationToken);
        return Ok(result);
    }

    /// <summary>Create a new product. Admin only.</summary>
    [HttpPost]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType(typeof(Guid), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> CreateProduct([FromBody] CreateProductCommand command,CancellationToken cancellationToken)
    {
        var productId = await _mediator.Send(command, cancellationToken);

        // IN: 201 Created with Location header pointing to the new resource.
        // CreatedAtAction generates: Location: /api/v1/products/{id}
        // This is the correct REST response for a successful POST that creates a resource.
        return CreatedAtAction(nameof(GetProductById), new { id = productId },productId);
    }

    /// <summary>Update an existing product. Admin only.</summary>
    [HttpPut("{id:guid}")]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> UpdateProduct(Guid id,[FromBody] UpdateProductCommand command, CancellationToken cancellationToken)
    {
        // IN: Bind Id from the route and set it on the command.
        // The route Id is the authoritative source — the body Id is ignored
        // if present, preventing confusion where route and body Ids differ.
        // REST convention: the resource identifier is in the URL, not the body.
        command.Id = id;

        await _mediator.Send(command, cancellationToken);

        // IN: 204 No Content for successful updates — no payload returned.
        // The client already has the resource — it sent the update.
        // 200 with the updated resource is also acceptable but adds a DB read.
        return NoContent();
    }

    /// <summary>Delete a product. Admin only.</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> DeleteProduct(Guid id, CancellationToken cancellationToken)
    {
        await _mediator.Send(new DeleteProductCommand(id), cancellationToken);
        return NoContent();
    }
}
