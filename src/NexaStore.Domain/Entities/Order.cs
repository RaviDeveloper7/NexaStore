using NexaStore.Domain.Enums;
using NexaStore.Domain.Events;

namespace NexaStore.Domain.Entities;

public class Order : BaseEntity
{
    public Guid CustomerId { get; set; }

    // IN: Status is an enum for type safety; stored as int in DB.
    public OrderStatus Status { get; set; }

    // IN: Denormalized TotalAmount for read performance (avoids re-summing on every read).
    public decimal TotalAmount { get; set; }

    // IN: Private setter enforces Aggregate Root pattern — all changes go through AddItem().
    public ICollection<OrderItem> Items { get; private set; } = new List<OrderItem>();

    // IN: Domain events raised when significant Order state changes occur.
    private readonly List<IDomainEvent> _domainEvents = new();

    // Read-only exposure of domain events
    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    public void AddDomainEvent(IDomainEvent domainEvent)
    {
        _domainEvents.Add(domainEvent);
    }

    public void ClearDomainEvents()
    {
        _domainEvents.Clear();
    }

    public void AddItem(OrderItem item)
    {
        if (Items.Any(i => i.ProductId == item.ProductId))
            throw new Exception($"Product with ID {item.ProductId} is already in the order.");

        Items.Add(item);
        // Recalculate total on item addition
        TotalAmount = Items.Sum(i => i.Quantity * i.UnitPrice);
    }
}
