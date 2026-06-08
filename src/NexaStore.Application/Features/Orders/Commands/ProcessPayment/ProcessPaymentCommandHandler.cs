// ProcessPaymentCommandHandler.cs — validates, records, and confirms payment.
// IN: This handler demonstrates four important patterns:
//
// 1. Amount validation  — payment must match order total (no underpayment)
// 2. Idempotency        — if already paid, return the existing Payment Id
// 3. Status transition  — Pending → Confirmed after successful payment
// 4. Ownership          — Customer can only pay for their own orders
//
// IN: Where would the payment gateway call go?
// Before Step 4 (create the Payment entity), inject IPaymentGateway
// (interface defined in Application, implemented in Infrastructure):
//
//   var gatewayResult = await _paymentGateway.ChargeAsync(
//       amount: command.Amount,
//       method: command.Method,
//       orderId: order.Id,
//       cancellationToken);
//
//   if (!gatewayResult.Succeeded)
//       throw new BadRequestException($"Payment failed: {gatewayResult.ErrorMessage}");
//
//   // Then create the Payment record with PaymentStatus.Completed

using MediatR;
using NexaStore.Application.Common.Interfaces.Identity;
using NexaStore.Application.Common.Interfaces.Persistence;
using NexaStore.Domain.Entities;
using NexaStore.Domain.Enums;
using NexaStore.Domain.Exceptions;

namespace NexaStore.Application.Features.Orders.Commands.ProcessPayment;

public class ProcessPaymentCommandHandler
    : IRequestHandler<ProcessPaymentCommand, Guid>
{
    private readonly IOrderRepository _orderRepository;
    private readonly IGenericRepository<Payment> _paymentRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserService _currentUserService;

    public ProcessPaymentCommandHandler(
        IOrderRepository orderRepository,
        IGenericRepository<Payment> paymentRepository,
        IUnitOfWork unitOfWork,
        ICurrentUserService currentUserService)
    {
        _orderRepository = orderRepository;
        _paymentRepository = paymentRepository;
        _unitOfWork = unitOfWork;
        _currentUserService = currentUserService;
    }

    public async Task<Guid> Handle(ProcessPaymentCommand command,CancellationToken cancellationToken)
    {
        // =====================================================================
        // STEP 1: Load the order
        // =====================================================================

        // GetByIdAsync is sufficient — payment processing doesn't need Items.
        // We only need Order.TotalAmount, Order.Status, and Order.CustomerId.
        var order = await _orderRepository
            .GetByIdAsync(command.OrderId, cancellationToken)
            ?? throw new NotFoundException(nameof(Order), command.OrderId);

        // =====================================================================
        // STEP 2: Ownership enforcement (Customer only)
        // =====================================================================

        if (!_currentUserService.IsAdmin)
        {
            var callerId = _currentUserService.UserId;

            if (!Guid.TryParse(callerId, out var callerGuid) ||
                order.CustomerId != callerGuid)
                throw new UnauthorizedAccessException(
                    "You do not have permission to process payment for this order.");

        }

        // =====================================================================
        // STEP 3: Order status must be Pending or Confirmed
        // =====================================================================

        // IN: Payment is only meaningful for active orders.
        // Delivered = already paid. Cancelled = no payment needed. Shipped = already paid.
        var payableStatuses = new HashSet<OrderStatus>
        {
            OrderStatus.Pending,
            OrderStatus.Confirmed
        };

        if (!payableStatuses.Contains(order.Status))
            throw new BadRequestException(
                $"Payment cannot be processed. Order status is '{order.Status}'. " +
                "Only Pending and Confirmed orders can be paid.");

        // =====================================================================
        // STEP 4: Idempotency — check for existing completed payment
        // =====================================================================

        // IN: If the client retries a payment request (network timeout,
        // double-click), we must not charge twice. Check for an existing Completed
        // payment first. If found, return its Id — same result, no duplicate charge.
        // This is the "natural idempotency key" approach — the OrderId is the key.
        // One order = one completed payment. Any retry returns the existing one.
        var existingPayment = await _paymentRepository
            .GetAsync(
                p => p.OrderId == command.OrderId &&
                     p.Status == PaymentStatus.Completed,
                cancellationToken);

        if (existingPayment.Any())
        {
            // Return the existing payment Id — no new charge, no error
            return existingPayment.First().Id;
        }

        // =====================================================================
        // STEP 5: Amount validation
        // =====================================================================

        // IN: Validate the payment amount matches the order total.
        // Using Math.Abs and a tolerance (0.01) instead of exact equality
        // because decimal arithmetic in distributed systems can introduce
        // tiny rounding differences. A 1-cent tolerance is safe for this use case.
        // In a real gateway integration, the gateway returns the charged amount —
        // compare the gateway's amount, not the client-submitted amount.
        if (Math.Abs(command.Amount - order.TotalAmount) > 0.01m)
            throw new BadRequestException(
                $"Payment amount ({command.Amount:C}) does not match " +
                $"the order total ({order.TotalAmount:C}).");

        // =====================================================================
        // STEP 6: Validate payment method
        // =====================================================================

        // INTERVIEW: Whitelist approach — only accept known payment methods.
        // Never trust client-supplied strings without validation.
        // A blacklist ("reject anything that looks like SQL") is always weaker
        // than a whitelist ("accept only these exact values").
        var validMethods = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "CreditCard",
            "DebitCard",
            "PayPal",
            "BankTransfer",
            "CashOnDelivery"
        };

        if (!validMethods.Contains(command.Method))
            throw new BadRequestException(
                $"Invalid payment method '{command.Method}'. " +
                $"Accepted methods: {string.Join(", ", validMethods)}.");

        // =====================================================================
        // STEP 7: Create the Payment record
        // =====================================================================

        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            OrderId = command.OrderId,
            Amount = command.Amount,
            // IN: In a real system with a payment gateway, Status would be
            // set based on the gateway's response:
            //   gatewayResult.Succeeded → PaymentStatus.Completed
            //   !gatewayResult.Succeeded → PaymentStatus.Failed
            // Here we mark Completed immediately (simulated successful gateway call).
            Status = PaymentStatus.Completed,
            Method = command.Method
        };

        await _paymentRepository.AddAsync(payment, cancellationToken);

        // =====================================================================
        // STEP 8: Advance order status to Confirmed
        // =====================================================================

        // IN: Successful payment automatically confirms the order.
        // This is a business rule: payment received = order confirmed.
        // We bypass UpdateOrderStatusCommandHandler here intentionally —
        // that handler is for Admin manual status updates via the API.
        // This is an internal status transition triggered by a business event (payment).
        // Calling one handler from another via MediatR is possible but creates
        // tight coupling between commands — direct state change is cleaner here.
        if (order.Status == OrderStatus.Pending)
        {
            // Only advance Pending → Confirmed.
            // If already Confirmed (e.g. Admin confirmed before payment),
            // don't change status — it's already in the right state.
            order.Status = OrderStatus.Confirmed;
        }

        // =====================================================================
        // STEP 9: Commit atomically
        // =====================================================================

        // IN: ONE SaveChangesAsync commits:
        // - INSERT INTO Payments (...)
        // - UPDATE Orders SET Status = Confirmed WHERE Id = @id  (if was Pending)
        // Both succeed or both roll back. A payment record without a status
        // update would be a data inconsistency — atomic commit prevents it.
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return payment.Id;
    }
}
