// CategoriesController.cs — read-only category listing.
// IN: Categories are reference data — seeded via EF migrations.
// No create/update/delete endpoints for now — categories are managed
// via migrations, not the API. This is a deliberate scope decision.
// Adding full CRUD for categories follows the exact same pattern as Products.

using Asp.Versioning;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using NexaStore.Application.Common.Interfaces.Persistence;
using NexaStore.Domain.Entities;

namespace NexaStore.Api.Controllers.v1;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/categories")]
public class CategoriesController : ControllerBase
{
    private readonly IGenericRepository<Category> _categoryRepository;

    // IN: Categories are simple reference data — no CQRS overhead needed.
    // Injecting IGenericRepository<Category> directly is acceptable here
    // because there is no business logic — just a straight DB read.
    // No handler, no command, no validator — just fetch and return.
    // This demonstrates pragmatic architecture: CQRS where it adds value,
    // direct repository access where it doesn't.
    public CategoriesController(IGenericRepository<Category> categoryRepository)
    {
        _categoryRepository = categoryRepository;
    }

    /// <summary>Get all product categories.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<CategoryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCategories(CancellationToken cancellationToken)
    {
        var categories = await _categoryRepository.GetAllAsync(cancellationToken);

        var dtos = categories
            .Select(c => new CategoryDto(c.Id, c.Name, c.Description))
            .ToList();

        return Ok(dtos);
    }
}

// Inline DTO — simple record for category list response
public record CategoryDto(Guid Id, string Name, string? Description);
