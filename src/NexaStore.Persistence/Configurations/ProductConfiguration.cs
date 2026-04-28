using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NexaStore.Domain.Entities;

namespace NexaStore.Persistence.Configurations;

public class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    // Hardcoded category GUIDs — must match CategoryConfiguration seed data exactly
    // INTERVIEW: Magic strings in seed data are unavoidable — extract to constants
    // so both configs reference the same values and a typo can't cause an FK violation.
    private static readonly Guid ElectronicsId = new("10000000-0000-0000-0000-000000000001");
    private static readonly Guid ClothingId = new("10000000-0000-0000-0000-000000000002");
    private static readonly Guid BooksId = new("10000000-0000-0000-0000-000000000003");
    private static readonly Guid HomeGardenId = new("10000000-0000-0000-0000-000000000004");
    private static readonly Guid SportsId = new("10000000-0000-0000-0000-000000000005");

    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("Products");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Id)
            .IsRequired()
            .ValueGeneratedNever();

        builder.Property(p => p.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(p => p.Description)
            .HasMaxLength(2000);

        builder.Property(p => p.Price)
            .IsRequired()
            .HasColumnType("decimal(18,2)");

        builder.Property(p => p.StockQuantity)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(p => p.CategoryId)
            .IsRequired();

        builder.Property(p => p.CreatedAt)
            .IsRequired();

        builder.Property(p => p.UpdatedAt)
            .IsRequired(false);

        builder.HasIndex(p => p.Name)
            .HasDatabaseName("IX_Products_Name");

        builder.HasIndex(p => p.CategoryId)
            .HasDatabaseName("IX_Products_CategoryId");

        builder.HasIndex(p => new { p.CategoryId, p.Name })
            .HasDatabaseName("IX_Products_CategoryId_Name");

        builder.ToTable(t => t.HasCheckConstraint("CK_Products_Price", "[Price] > 0"));
        builder.ToTable(t => t.HasCheckConstraint("CK_Products_StockQuantity", "[StockQuantity] >= 0"));

        // --- Seed Data ---
        // INTERVIEW: Seed product prices use decimal literals (m suffix).
        // Omitting the m suffix makes it a double — the compiler will warn,
        // and more importantly it is semantically wrong for money values.
        builder.HasData(

            // --- Electronics (3 products) ---
            new Product
            {
                Id = new Guid("20000000-0000-0000-0000-000000000001"),
                Name = "Samsung Galaxy S24 Ultra",
                Description = "6.8-inch QHD+ Dynamic AMOLED, 200MP camera, S Pen included",
                Price = 1299.99m,
                StockQuantity = 50,
                CategoryId = ElectronicsId,
                CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            },
            new Product
            {
                Id = new Guid("20000000-0000-0000-0000-000000000002"),
                Name = "Apple MacBook Pro 14-inch M3",
                Description = "Apple M3 chip, 16GB RAM, 512GB SSD, Liquid Retina XDR display",
                Price = 1999.99m,
                StockQuantity = 30,
                CategoryId = ElectronicsId,
                CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            },
            new Product
            {
                Id = new Guid("20000000-0000-0000-0000-000000000003"),
                Name = "Sony WH-1000XM5 Headphones",
                Description = "Industry-leading noise cancellation, 30-hour battery life",
                Price = 349.99m,
                StockQuantity = 100,
                CategoryId = ElectronicsId,
                CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            },

            // --- Clothing (3 products) ---
            new Product
            {
                Id = new Guid("20000000-0000-0000-0000-000000000004"),
                Name = "Nike Air Max 270",
                Description = "Lightweight running shoe with Max Air unit for all-day comfort",
                Price = 149.99m,
                StockQuantity = 200,
                CategoryId = ClothingId,
                CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            },
            new Product
            {
                Id = new Guid("20000000-0000-0000-0000-000000000005"),
                Name = "Levi's 501 Original Jeans",
                Description = "Classic straight fit, 100% cotton denim, iconic button fly",
                Price = 79.99m,
                StockQuantity = 150,
                CategoryId = ClothingId,
                CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            },
            new Product
            {
                Id = new Guid("20000000-0000-0000-0000-000000000006"),
                Name = "The North Face Thermoball Jacket",
                Description = "Lightweight insulated jacket, compressible, water-resistant",
                Price = 199.99m,
                StockQuantity = 75,
                CategoryId = ClothingId,
                CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            },

            // --- Books (3 products) ---
            new Product
            {
                Id = new Guid("20000000-0000-0000-0000-000000000007"),
                Name = "Clean Architecture by Robert C. Martin",
                Description = "A craftsman's guide to software structure and design",
                Price = 39.99m,
                StockQuantity = 300,
                CategoryId = BooksId,
                CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            },
            new Product
            {
                Id = new Guid("20000000-0000-0000-0000-000000000008"),
                Name = "Designing Data-Intensive Applications",
                Description = "The big ideas behind reliable, scalable and maintainable systems",
                Price = 49.99m,
                StockQuantity = 250,
                CategoryId = BooksId,
                CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            },
            new Product
            {
                Id = new Guid("20000000-0000-0000-0000-000000000009"),
                Name = "The Pragmatic Programmer",
                Description = "Your journey to mastery, 20th anniversary edition",
                Price = 44.99m,
                StockQuantity = 200,
                CategoryId = BooksId,
                CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            },

            // --- Home & Garden (2 products) ---
            new Product
            {
                Id = new Guid("20000000-0000-0000-0000-000000000010"),
                Name = "Instant Pot Duo 7-in-1",
                Description = "Electric pressure cooker, slow cooker, rice cooker and more",
                Price = 89.99m,
                StockQuantity = 120,
                CategoryId = HomeGardenId,
                CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            },
            new Product
            {
                Id = new Guid("20000000-0000-0000-0000-000000000011"),
                Name = "Dyson V15 Detect Vacuum",
                Description = "Laser detects invisible dust, HEPA filtration, 60-min runtime",
                Price = 749.99m,
                StockQuantity = 40,
                CategoryId = HomeGardenId,
                CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            },

            // --- Sports & Outdoors (2 products) ---
            new Product
            {
                Id = new Guid("20000000-0000-0000-0000-000000000012"),
                Name = "Garmin Forerunner 265 GPS Watch",
                Description = "AMOLED display, advanced running metrics, heart rate monitor",
                Price = 449.99m,
                StockQuantity = 60,
                CategoryId = SportsId,
                CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            },
            new Product
            {
                Id = new Guid("20000000-0000-0000-0000-000000000013"),
                Name = "Bowflex SelectTech 552 Dumbbells",
                Description = "Adjustable 5 to 52.5 lbs, replaces 15 sets of weights",
                Price = 399.99m,
                StockQuantity = 45,
                CategoryId = SportsId,
                CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            }
        );
    }
}
