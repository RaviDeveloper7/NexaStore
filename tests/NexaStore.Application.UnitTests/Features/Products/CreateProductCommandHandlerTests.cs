// CreateProductCommandHandlerTests.cs — proves category validation, entity
// construction, and cache invalidation on product creation.

using FluentAssertions;
using NexaStore.Application.Features.Products.Commands.CreateProduct;
using NexaStore.Application.UnitTests.Common;
using NexaStore.Application.UnitTests.Mocks;
using NexaStore.Domain.Entities;
using NexaStore.Domain.Exceptions;
using Xunit;

namespace NexaStore.Application.UnitTests.Features.Products;

public class CreateProductCommandHandlerTests
{
    private readonly MockProductRepository _productRepository = new();
    private readonly MockGenericRepository<Category> _categoryRepository = new();
    private readonly MockUnitOfWork _unitOfWork = new();
    private readonly MockCacheService _cacheService = new();

    private readonly CreateProductCommandHandler _handler;

    public CreateProductCommandHandlerTests()
    {
        _handler = new CreateProductCommandHandler(
            _productRepository,
            _categoryRepository,
            _unitOfWork,
            _cacheService);
    }

    [Fact]
    public async Task Handle_ValidCommand_CreatesProductAndReturnsNewId()
    {
        // Arrange
        var category = TestDataBuilder.CreateCategory();
        _categoryRepository.Items.Add(category);

        var command = new CreateProductCommand
        {
            Name = "Wireless Mouse",
            Description = "Ergonomic wireless mouse",
            Price = 29.99m,
            StockQuantity = 100,
            CategoryId = category.Id
        };

        // Act
        var productId = await _handler.Handle(command, CancellationToken.None);

        // Assert
        productId.Should().NotBeEmpty();

        var savedProduct = _productRepository.Products.Should().ContainSingle().Subject;
        savedProduct.Name.Should().Be("Wireless Mouse");
        savedProduct.Price.Should().Be(29.99m);
        savedProduct.CategoryId.Should().Be(category.Id);

        _unitOfWork.SaveChangesCallCount.Should().Be(1);
    }

    [Fact]
    public async Task Handle_ValidCommand_InvalidatesProductListCache()
    {
        // Arrange — IN: proves the cache-busting contract every write handler
        // must honour: after any mutation, all "products:" prefixed cache
        // entries must be removed so stale paginated lists are never served.
        var category = TestDataBuilder.CreateCategory();
        _categoryRepository.Items.Add(category);

        // Pre-populate the cache to simulate an existing cached list
        await _cacheService.SetAsync("products:p=1:s=10", new List<string>());

        var command = new CreateProductCommand
        {
            Name = "Keyboard",
            Price = 49.99m,
            CategoryId = category.Id
        };

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        _cacheService.RemoveByPrefixCalls.Should().ContainSingle()
            .Which.Should().Be("products:");
    }

    [Fact]
    public async Task Handle_CategoryDoesNotExist_ThrowsNotFoundException()
    {
        // Arrange — no category seeded
        var command = new CreateProductCommand
        {
            Name = "Orphan Product",
            Price = 19.99m,
            CategoryId = Guid.NewGuid() // does not exist
        };

        // Act
        var act = async () => await _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>();

        // Nothing should be persisted when the business rule check fails
        _productRepository.Products.Should().BeEmpty();
        _unitOfWork.SaveChangesCallCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_ValidCommand_GeneratesNewGuidForProductId()
    {
        // IN: Proves app-generated Ids — ValueGeneratedNever() at the DB level
        // means the application MUST supply the Id, never rely on the database.
        var category = TestDataBuilder.CreateCategory();
        _categoryRepository.Items.Add(category);

        var command = new CreateProductCommand
        {
            Name = "Test",
            Price = 10m,
            CategoryId = category.Id
        };

        // Act
        var productId = await _handler.Handle(command, CancellationToken.None);

        // Assert
        var savedProduct = _productRepository.Products.Single();
        savedProduct.Id.Should().Be(productId);
        savedProduct.Id.Should().NotBe(Guid.Empty);
    }
}
