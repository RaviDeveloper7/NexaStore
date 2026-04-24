// IDomainEvent.cs — the marker interface for all domain events in the system.
// INTERVIEW: Marker interfaces define a contract with no members.
// They allow type-safe filtering: "give me all IDomainEvent implementations".
// MediatR can also use this as a base for INotification if you ever want
// in-process event dispatch alongside the Outbox.

using MediatR;

namespace NexaStore.Domain.Events;

// INTERVIEW: Inheriting from INotification allows MediatR to dispatch
// domain events in-process if needed — keeps the door open for
// in-process handlers alongside the async Outbox flow.
public interface IDomainEvent : INotification
{
    // Every domain event must carry the timestamp it occurred.
    // INTERVIEW: OccurredOn enables event sourcing patterns later
    // and is essential for audit logs and debugging event sequences.
    DateTime OccurredOn { get; }
}
