// Payment.cs — records a payment attempt for an Order.
// INTERVIEW: Payment is separate from Order because one Order could theoretically
// have multiple payment attempts (failed then retry). Normalising it out also
// means you can query payment history independently.

using NexaStore.Domain.Enums;

namespace NexaStore.Domain.Entities;

public class Payment : BaseEntity
{
    // Which order this payment is for
    public Guid OrderId { get; set; }

    // Navigation back to parent order
    public Order Order { get; set; } = null!;

    // Payment amount — should match Order.TotalAmount, validated in handler
    public decimal Amount { get; set; }

    // INTERVIEW: Status as enum — Pending → Completed or Failed → Refunded
    public PaymentStatus Status { get; set; }

    // How the customer paid — stored as a string for flexibility
    // e.g. "CreditCard", "PayPal", "BankTransfer"
    // INTERVIEW: Could be an enum but string gives extensibility without migration
    public string Method { get; set; } = string.Empty;
}
