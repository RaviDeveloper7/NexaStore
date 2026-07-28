// GetProductsQueryHandlerTests.cs — proves the Cache-Aside Pattern implementation:
// cache miss queries the DB and populates cache; cache hit skips the DB entirely.

using FluentAssertions;
using MapsterMapper;
using Mapster;
using NexaStore.Application.Common.Mappings;
using NexaStore.Application.Features.Products.Queries.GetProducts;
using NexaStore.Application.UnitTests.Common;
using NexaStore.Application.UnitTests.Mocks;
using Xunit;

namespace NexaStore.Application.UnitTests.Features.Products;

public class GetProductsQueryHandlerTests
{
    private readonly MockProductRepository _productRepository = new();
    private readonly MockCacheService _cacheService = new();
    private readonly IMapper _mapper;

    private readonly GetProductsQueryHandler _handler;

    public GetProductsQueryHandlerTests()
    {
        // IN: Build a real Mapster config for the test — not mocked.
        // Mapping logic (CategoryName cross-property mapping) is exactly what
        // we want to verify actually works, so we use the real ProductMappingProfile
        // rather than mocking IMapper and returning a hand-built DTO.
        // A mocked mapper would make this test pass even if the real mapping
        // profile were broken — defeating its purpose.
        var config = new TypeAdapterConfig();
        config.Scan(typeof(ProductMappingProfile).Assembly);
       // _mapper = new ServiceMapper(config); // commented, need to be fixed 

        _handler = new GetProductsQueryHandler(
            _productRepository,
            _cacheService,
            _mapper);
    }

    [Fact]
    public async Task Handle_CacheMiss_QueriesRepositoryAndPopulatesCache()
    {
        // Arrange
        var category = TestDataBuilder.CreateCategory(name: "Books");
        var product = TestDataBuilder.CreateProduct(
            name: "Clean Code", categoryId: category.Id);
        product.Category = category; // simulate the .Include() join

        _productRepository.Products.Add(product);

        var query = new GetProductsQuery { PageNumber = 1, PageSize = 10 };

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.Items.Should().ContainSingle();
        result.Items.Single().Name.Should().Be("Clean Code");
        result.Items.Single().CategoryName.Should().Be("Books");

        // Cache was populated after the DB query (write-through on miss)
        _cacheService.SetCalls.Should().ContainSingle();
    }

    [Fact]
    public async Task Handle_CacheHit_SkipsRepositoryEntirely()
    {
        // Arrange — seed a product, run the handler once to warm the cache
        var product = TestDataBuilder.CreateProduct(name: "First Call Product");
        product.Category = TestDataBuilder.CreateCategory();
        _productRepository.Products.Add(product);

        var query = new GetProductsQuery { PageNumber = 1, PageSize = 10 };

        await _handler.Handle(query, CancellationToken.None); // warms cache

        // Now remove the product from the repository — if the handler hits
        // the DB again, the result would be empty. If it hits cache, the
        // original cached result is returned unchanged.
        _productRepository.Products.Clear();

        // Act — second call with IDENTICAL query parameters
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert — IN: this is the critical proof of cache-aside behaviour.
        // Despite the repository being empty, the result still contains the
        // product because it came from cache, not the DB.
        result.Items.Should().ContainSingle();
        result.Items.Single().Name.Should().Be("First Call Product");
    }

    [Fact]
    public async Task Handle_DifferentQueryParameters_ProducesDifferentCacheKeys()
    {
        // IN: Proves the cache key includes every filter parameter.
        // Without this, two different queries could collide on the same
        // cache key and return each other's results — a real production bug class.
        var product = TestDataBuilder.CreateProduct();
        product.Category = TestDataBuilder.CreateCategory();
        _productRepository.Products.Add(product);

        var queryPage1 = new GetProductsQuery { PageNumber = 1, PageSize = 10 };
        var queryPage2 = new GetProductsQuery { PageNumber = 2, PageSize = 10 };

        // Act
        await _handler.Handle(queryPage1, CancellationToken.None);
        await _handler.Handle(queryPage2, CancellationToken.None);

        // Assert — two distinct cache keys were written, one per page
        _cacheService.SetCalls.Should().HaveCount(2);
        _cacheService.SetCalls.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task Handle_SearchTermFilter_ReturnsOnlyMatchingProducts()
    {
        // Arrange
        var category = TestDataBuilder.CreateCategory();
        var matching = TestDataBuilder.CreateProduct(name: "Wireless Mouse");
        var nonMatching = TestDataBuilder.CreateProduct(name: "USB Cable");
        matching.Category = category;
        nonMatching.Category = category;

        _productRepository.Products.AddRange(new[] { matching, nonMatching });

        var query = new GetProductsQuery
        {
            PageNumber = 1,
            PageSize = 10,
            SearchTerm = "Wireless"
        };

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.Items.Should().ContainSingle();
        result.Items.Single().Name.Should().Be("Wireless Mouse");
    }
}
