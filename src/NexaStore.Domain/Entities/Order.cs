// Order.cs — the most important aggregate in the system.
// INTERVIEW: Order is an Aggregate Root — it owns OrderItems and controls
// all state transitions. No code outside Order can add items or change status
// directly. All changes go through methods on this class.
// Domain events are raised here — Order decides when something meaningful happened.

using NexaStore.Domain.Enums;
using NexaStore.Domain.Events;

namespace NexaStore.Domain.Entities;

public class Order : BaseEntity
{
    // The customer who placed this order — links to Identity (ApplicationUser)
    public Guid CustomerId { get; set; }

    // INTERVIEW: Status is an enum, not a magic string — type safety at compile time.
    // Stored as int in DB via EF configuration.
    public OrderStatus Status { get; set; }

    // Calculated and stored — avoids re-summing items on every read
    // INTERVIEW: Denormalization here is intentional for read performance
    public decimal TotalAmount { get; set; }

    // Private setter — only Order controls its items collection
    // INTERVIEW: This enforces the Aggregate Root pattern.
    // External code calls order.AddItem(...) not order.Items.Add(...)
    public ICollection<OrderItem> Items { get; private set; } = new List<OrderItem>();

    // Domain events — raised when something significant happens to this Order.
    // INTERVIEW: Domain events decouple the Order aggregate from side effects.
    // The Outbox Processor reads these and publishes to Service Bus.
    // Using a private backing field to prevent external mutation.
    private readonly List<IDomainEvent> _domainEvents = new();

    // Read-only exposure — external code can read but not mutate
    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    // Called by PlaceOrderCommandHandler to queue an event on this aggregate
    public void AddDomainEvent(IDomainEvent domainEvent)
    {
        _domainEvents.Add(domainEvent);
    }

    // Clears events after they have been persisted to the Outbox table
    // INTERVIEW: Events are cleared after save so they don't fire twice
    // if SaveChanges is called more than once in a unit of work
    public void ClearDomainEvents()
    {
        _domainEvents.Clear();
    }

    // Factory method for adding items — keeps the aggregate in control
    public void AddItem(OrderItem item)
    {
        Items.Add(item);

        // Recalculate total every time an item is added
        TotalAmount = Items.Sum(i => i.Quantity * i.UnitPrice);
    }
}
