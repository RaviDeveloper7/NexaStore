// CategoryConfiguration.cs — Fluent API config for the Category entity.
// INTERVIEW: Fluent API is preferred over Data Annotations because:
// 1. Keeps the Domain entity clean — no EF attributes polluting domain objects
// 2. More powerful — can express complex relationships Data Annotations can't
// 3. Follows the Separation of Concerns principle

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NexaStore.Domain.Entities;

namespace NexaStore.Persistence.Configurations;

public class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        // --- Table ---
        builder.ToTable("Categories");

        // --- Primary Key ---
        // INTERVIEW: EF picks up "Id" as PK by convention, but being explicit
        // is better practice in production code — no surprises if you rename the property.
        builder.HasKey(c => c.Id);

        // --- Properties ---
        builder.Property(c => c.Id)
            .IsRequired()
            .ValueGeneratedNever(); // INTERVIEW: We generate Guids in the app, not the DB.
                                    // DB-generated Guids use NEWSEQUENTIALID() which is fine
                                    // but app-generated means we know the ID before the insert.

        builder.Property(c => c.Name)
            .IsRequired()
            .HasMaxLength(100);    // Reasonable cap — prevents accidental large strings

        builder.Property(c => c.Description)
            .HasMaxLength(500);    // Optional field, nullable by default

        builder.Property(c => c.CreatedAt)
            .IsRequired();

        builder.Property(c => c.UpdatedAt)
            .IsRequired(false);   // Nullable — not yet updated at creation time

        // --- Indexes ---
        // INTERVIEW: Index on Name because filtering categories by name is common.
        // IsUnique = category names must be distinct — business rule enforced at DB level.
        builder.HasIndex(c => c.Name)
            .IsUnique()
            .HasDatabaseName("IX_Categories_Name");

        // --- Relationships ---
        // One Category → many Products
        // INTERVIEW: DeleteBehavior.Restrict prevents cascading deletes.
        // You should not delete a Category if Products still reference it.
        // The handler should throw BadRequestException if products exist.
        builder.HasMany(c => c.Products)
            .WithOne(p => p.Category)
            .HasForeignKey(p => p.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
