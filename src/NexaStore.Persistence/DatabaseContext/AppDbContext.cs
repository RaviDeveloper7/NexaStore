using MediatR;
using Microsoft.EntityFrameworkCore;
using NexaStore.Domain.Entities;

namespace NexaStore.Persistence.DatabaseContext;

public class AppDbContext : DbContext
{
    private readonly IMediator _mediator;

    // IN: IMediator enables in-process domain event dispatch after SaveChanges.
    public AppDbContext(DbContextOptions<AppDbContext> options, IMediator mediator)
        : base(options)
    {
        _mediator = mediator;
    }

    public DbSet<Product> Products { get; set; }
    public DbSet<Category> Categories { get; set; }
    public DbSet<Order> Orders { get; set; }
    public DbSet<OrderItem> OrderItems { get; set; }
    public DbSet<Payment> Payments { get; set; }
    public DbSet<OutboxMessage> OutboxMessages { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // IN: ApplyConfigurationsFromAssembly scans for IEntityTypeConfiguration implementations.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }

    // IN: Override SaveChangesAsync for audit timestamps and domain event dispatch.
    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        // Auto-set CreatedAt / UpdatedAt for all BaseEntity entries
        foreach (var entry in ChangeTracker.Entries<BaseEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    if (entry.Entity.Id == Guid.Empty)
                        entry.Entity.Id = Guid.NewGuid();
                    entry.Entity.CreatedAt = DateTime.UtcNow;
                    break;

                case EntityState.Modified:
                    entry.Entity.UpdatedAt = DateTime.UtcNow;
                    break;
            }
        }

        // Persist to database first
        var result = await base.SaveChangesAsync(cancellationToken);

        // IN: Dispatch events only after successful save to maintain consistency.
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
        // IN: These are in-process domain events. The Outbox handles
        // the cross-process/async delivery to Azure Service Bus separately.
        foreach (var domainEvent in domainEvents)
        {
            await _mediator.Publish(domainEvent, cancellationToken);
        }
    }
}
