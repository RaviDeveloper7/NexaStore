using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NexaStore.Domain.Entities;

namespace NexaStore.Persistence.Configurations;

public class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.ToTable("Categories");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Id)
            .IsRequired()
            .ValueGeneratedNever();

        builder.Property(c => c.Name)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(c => c.Description)
            .HasMaxLength(500);

        builder.Property(c => c.CreatedAt)
            .IsRequired();

        builder.Property(c => c.UpdatedAt)
            .IsRequired(false);

        builder.HasIndex(c => c.Name)
            .IsUnique()
            .HasDatabaseName("IX_Categories_Name");

        builder.HasMany(c => c.Products)
            .WithOne(p => p.Category)
            .HasForeignKey(p => p.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        // --- Seed Data ---
        // INTERVIEW: HasData() is EF's built-in seeding mechanism.
        // Seed rows are included directly in the migration — they are
        // idempotent (EF tracks them by Id and only inserts if missing).
        // Important: Ids must be hardcoded Guids, never Guid.NewGuid() —
        // that would generate a different Guid every time the migration runs,
        // causing EF to try to insert duplicate rows on every update.
        builder.HasData(
            new Category
            {
                Id = new Guid("10000000-0000-0000-0000-000000000001"),
                Name = "Electronics",
                Description = "Smartphones, laptops, tablets and accessories",
                CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            },
            new Category
            {
                Id = new Guid("10000000-0000-0000-0000-000000000002"),
                Name = "Clothing",
                Description = "Men's, women's and children's apparel",
                CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            },
            new Category
            {
                Id = new Guid("10000000-0000-0000-0000-000000000003"),
                Name = "Books",
                Description = "Fiction, non-fiction, technical and educational books",
                CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            },
            new Category
            {
                Id = new Guid("10000000-0000-0000-0000-000000000004"),
                Name = "Home & Garden",
                Description = "Furniture, kitchen appliances and garden tools",
                CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            },
            new Category
            {
                Id = new Guid("10000000-0000-0000-0000-000000000005"),
                Name = "Sports & Outdoors",
                Description = "Fitness equipment, outdoor gear and sportswear",
                CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            }
        );
    }
}
