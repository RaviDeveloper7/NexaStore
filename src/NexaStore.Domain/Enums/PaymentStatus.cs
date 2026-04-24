// PaymentStatus.cs — all valid states for a Payment.
// INTERVIEW: Starting enums at 1, not 0. Zero is the default int value —
// if a Payment.Status is accidentally unset, it won't silently map to a
// valid state. A value of 0 means "not set", which is clearly wrong.

namespace NexaStore.Domain.Enums;

public enum PaymentStatus
{
    // Payment has been initiated but not yet confirmed
    Pending = 1,

    // Payment was successfully processed — terminal success state
    Completed = 2,

    // Payment attempt failed — customer should retry
    Failed = 3,

    // Payment was reversed after completion (e.g. chargeback or cancellation)
    Refunded = 4
}
