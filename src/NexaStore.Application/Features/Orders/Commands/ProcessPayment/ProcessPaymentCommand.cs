// ProcessPaymentCommand.cs — records a payment against an Order.
// IN: NexaStore does not integrate with a real payment gateway (Stripe, PayPal).
// That integration is a project in itself. Instead, this command demonstrates
// the PATTERN for payment processing — the gateway integration would be
// injected via an IPaymentGateway interface in Infrastructure, called from this handler.
//
// IN: Why a separate ProcessPayment command instead of paying during PlaceOrder?
// Separation of concerns. The order placement (stock reservation, event publishing)
// is independent of payment processing (gateway call, payment record).
// This allows:
// a) Pay later flows — place order, pay on delivery
// b) Payment retry — PlaceOrder once, retry ProcessPayment if first attempt fails
// c) Different payment methods for the same order
// d) Admin manually marking an order as paid (cash on delivery)

using MediatR;

namespace NexaStore.Application.Features.Orders.Commands.ProcessPayment;

public class ProcessPaymentCommand : IRequest<Guid>
{
    // IN: Returns Guid — the new Payment record's Id.
    // Client can use this to fetch payment details or track refunds later.
    public Guid OrderId { get; set; }

    // Payment method as a string — "CreditCard", "PayPal", "BankTransfer", "CashOnDelivery"
    // IN: String over enum for extensibility. New payment methods
    // don't require a schema migration or code deployment to add.
    // The trade-off: no compile-time exhaustiveness check.
    // For a portfolio project, string is a pragmatic choice.
    public string Method { get; set; } = string.Empty;

    // Amount the customer is paying — validated against Order.TotalAmount in handler
    // IN: Why accept Amount from the client at all?
    // Partial payments, split payments, or discount codes could make the
    // payment amount differ from TotalAmount. For NexaStore we validate they match.
    // In a real system, the gateway returns the charged amount — use that, not client input.
    public decimal Amount { get; set; }
}
