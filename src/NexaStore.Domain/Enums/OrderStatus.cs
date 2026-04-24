// OrderStatus.cs — all valid states for an Order.
// INTERVIEW: Enums over magic strings — compile-time safety, Intellisense support,
// and stored as int in the DB which is more efficient than varchar.
// The state machine is: Pending → Confirmed → Shipped → Delivered
//                                           ↘ Cancelled (from Pending or Confirmed)

namespace NexaStore.Domain.Enums;

public enum OrderStatus
{
    // Initial state when order is placed — awaiting confirmation
    Pending = 1,

    // Admin has confirmed the order — payment processed
    Confirmed = 2,

    // Physical goods dispatched to the customer
    Shipped = 3,

    // Customer has received the goods — terminal success state
    Delivered = 4,

    // Order was cancelled — either by customer or by the expiry function
    // INTERVIEW: OrderExpiryFunction cancels orders stuck in Pending > 24hrs
    Cancelled = 5
}
