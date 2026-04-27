// AppDbContext.cs — the single EF Core DbContext for the entire application.
// INTERVIEW: We use ONE DbContext for all domain entities. Identity gets its
// own separate IdentityDbContext (in the Identity layer) to keep concerns separated.
// AppDbContext knows nothing about ASP.NET Core Identity users.
//
// Key responsibilities:
// 1. Exposes DbSets for every entity
// 2. Applies all Fluent API configurations from the Configurations folder
// 3. Intercepts SaveChangesAsync to auto-set CreatedAt / UpdatedAt on BaseEntity
// 4. Dispatches domain events AFTER saving — events must not fire on a failed save

using MediatR;
using Microsoft.EntityFrameworkCore;
using NexaStore.Domain.Entities;

namespace NexaStore.Persistence.DatabaseContext;

public class AppDbContext : DbContext
{
    private readonly IMediator _mediator;

    // INTERVIEW: IMediator is injected here so domain events raised on
    // aggregates (Order.AddDomainEvent) can be dispatched in-process after
    // SaveChangesAsync. The Outbox also handles async cross-boundary delivery,
    // but in-process dispatch gives immediate local side effects if needed.
    public AppDbContext(DbContextOptions<AppDbContext> options, IMediator mediator)
        : base(options)
    {
        _mediator = mediator;
    }

    // --- DbSets — one per domain entity ---
    // INTERVIEW: DbSet<T> is the EF gateway to the underlying table.
    // Naming convention: plural of the entity name.
    public DbSet<Product> Products { get; set; }
    public DbSet<Category> Categories { get; set; }
    public DbSet<Order> Orders { get; set; }
    public DbSet<OrderItem> OrderItems { get; set; }
    public DbSet<Payment> Payments { get; set; }
    public DbSet<OutboxMessage> OutboxMessages { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // INTERVIEW: ApplyConfigurationsFromAssembly scans this assembly for
        // all classes implementing IEntityTypeConfiguration<T> and applies them.
        // This is cleaner than manually calling modelBuilder.ApplyConfiguration(new XConfig())
        // for every entity — adding a new config file is all you need.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }

    // INTERVIEW: Overriding SaveChangesAsync is the standard pattern for
    // cross-cutting concerns like audit timestamps and domain event dispatch.
    // This fires for EVERY save in the system — no handler needs to set these manually.
    public override async Task<int> SaveChangesAsync(
        CancellationToken cancellationToken = default)
    {
        // --- Audit timestamp interception ---
        // Walk every tracked entity that is BaseEntity and has been Added or Modified
        foreach (var entry in ChangeTracker.Entries<BaseEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    // Set both Id and CreatedAt if not already set
                    // INTERVIEW: Guid.NewGuid() here as a safety net —
                    // handlers should set Id themselves, but this prevents null IDs
                    // if they forget.
                    if (entry.Entity.Id == Guid.Empty)
                        entry.Entity.Id = Guid.NewGuid();

                    entry.Entity.CreatedAt = DateTime.UtcNow;
                    break;

                case EntityState.Modified:
                    // Only set UpdatedAt on modification — never overwrite CreatedAt
                    entry.Entity.UpdatedAt = DateTime.UtcNow;
                    break;
            }
        }

        // --- Persist everything to the database first ---
        var result = await base.SaveChangesAsync(cancellationToken);

        // --- Dispatch domain events AFTER successful save ---
        // INTERVIEW: Events must fire AFTER save, not before.
        // If we dispatched before save and the DB write failed, we'd have
        // published events for something that never happened — a consistency nightmare.
        await DispatchDomainEventsAsync(cancellationToken);

        return result;
    }

    private async Task DispatchDomainEventsAsync(CancellationToken cancellationToken)
    {
        // Find all tracked Order aggregates that have pending domain events
        var entitiesWithEvents = ChangeTracker
            .Entries<Order>()
            .Select(e => e.Entity)
            .Where(e => e.DomainEvents.Any())
            .ToList();

        // Collect all events before clearing — clearing mid-dispatch could
        // cause issues if an event handler triggers another save
        var domainEvents = entitiesWithEvents
            .SelectMany(e => e.DomainEvents)
            .ToList();

        // Clear events from aggregates so they don't fire again on the next save
        entitiesWithEvents.ForEach(e => e.ClearDomainEvents());

        // Dispatch each event via MediatR — in-process handlers receive these
        // INTERVIEW: These are in-process domain events. The Outbox handles
        // the cross-process/async delivery to Azure Service Bus separately.
        foreach (var domainEvent in domainEvents)
        {
            await _mediator.Publish(domainEvent, cancellationToken);
        }
    }
}
