// Category.cs — a simple aggregate that groups Products.
// INTERVIEW: Categories don't need domain events — they're reference data,
// not transactional. Only entities with business-critical state changes
// get domain events.

namespace NexaStore.Domain.Entities;

public class Category : BaseEntity
{
    // Name is required — enforced at DB level via EF Fluent config in Persistence
    public string Name { get; set; } = string.Empty;

    // Description is optional — marketing copy, can be null
    public string? Description { get; set; }

    // Navigation property — EF Core uses this for the relationship
    // INTERVIEW: We keep navigation props on both sides so EF can
    // build correct JOIN queries without explicit .Include() in every query
    public ICollection<Product> Products { get; set; } = new List<Product>();
}
