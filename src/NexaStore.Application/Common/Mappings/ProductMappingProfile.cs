// ProductMappingProfile.cs — Mapster configuration for Product entity.
// INTERVIEW: Mapster uses IRegister (not AutoMapper's Profile) for configuration.
// Register() is called once at startup via MappingExtensions.RegisterMapsterMappings().
// Mapster is faster than AutoMapper because it generates IL code at startup
// rather than using reflection on every map call.

using Mapster;
using NexaStore.Application.Features.Products.Queries.GetProductById;
using NexaStore.Application.Features.Products.Queries.GetProducts;
using NexaStore.Domain.Entities;

namespace NexaStore.Application.Common.Mappings;

public class ProductMappingProfile : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        // --- Product → ProductListDto ---
        config.NewConfig<Product, ProductListDto>()

            // INTERVIEW: Mapster handles same-name properties automatically.
            // We only need to configure properties that DON'T match by name.
            // CategoryName does not exist on Product — it lives on Product.Category.Name.
            // Map() tells Mapster exactly where to find it.
            .Map(dest => dest.CategoryName,
                 src => src.Category != null ? src.Category.Name : string.Empty)

            // StockQuantity, Price, Name, Id, CreatedAt — all auto-mapped by name
            // IsInStock — computed property on the DTO, not mapped (no setter)
            .IgnoreNonMapped(false);   // Don't silently ignore unmapped members in DEBUG

        // --- Product → ProductDetailDto ---
        config.NewConfig<Product, ProductDetailDto>()

            .Map(dest => dest.CategoryName,
                 src => src.Category != null ? src.Category.Name : string.Empty)

            // CategoryId auto-mapped by name
            // Description, Price, StockQuantity, CreatedAt, UpdatedAt — auto-mapped
            .IgnoreNonMapped(false);
    }
}
