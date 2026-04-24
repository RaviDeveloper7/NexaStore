// BaseEntity.cs — the root of every domain entity in the system.
// INTERVIEW: Every entity inherits from BaseEntity so Id, CreatedAt, UpdatedAt
// are never forgotten. Guid over int — distributed systems safe, no DB round-trip needed.
// UpdatedAt is nullable because it hasn't been updated at creation time.

namespace NexaStore.Domain.Entities;

public abstract class BaseEntity
{
    // INTERVIEW: Guid as primary key means IDs can be generated client-side
    // or in the application layer — no DB dependency for ID creation.
    public Guid Id { get; set; }

    // Set once at creation — never updated
    public DateTime CreatedAt { get; set; }

    // Null until the entity is first modified after creation
    public DateTime? UpdatedAt { get; set; }
}
