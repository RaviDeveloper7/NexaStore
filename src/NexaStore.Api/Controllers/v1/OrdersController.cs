// OrdersController.cs — order lifecycle management.
// IN: [Authorize] at class level — every order endpoint requires authentication.
// Unauthenticated users cannot place or view orders.
// Role-based access (Admin vs Customer) is enforced inside handlers,
// not with per-action [Authorize(Roles)] attributes — because the same
// endpoint (GetOrders, GetOrderById) serves both roles with different data.

using Asp.Versioning;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexaStore.Application.Common.Models;
using NexaStore.Application.Features.Orders.Commands.CancelOrder;
using NexaStore.Application.Features.Orders.Commands.PlaceOrder;
using NexaStore.Application.Features.Orders.Commands.ProcessPayment;
using NexaStore.Application.Features.Orders.Commands.UpdateOrderStatus;
using NexaStore.Application.Features.Orders.Queries.GetOrderById;
using NexaStore.Application.Features.Orders.Queries.GetOrders;
using NexaStore.Identity.Settings;

namespace NexaStore.Api.Controllers.v1;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/orders")]
[Authorize]  // All order endpoints require authentication
public class OrdersController : ControllerBase
{
    private readonly IMediator _mediator;

    public OrdersController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Get paged orders. Customers see their own orders. Admins see all orders.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<OrderListDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetOrders(
        [FromQuery] GetOrdersQuery query,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(query, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Get full order detail with line items.
    /// Customers can only access their own orders.
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(OrderDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetOrderById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new GetOrderByIdQuery(id), cancellationToken);
        return Ok(result);
    }

    /// <summary>Place a new order. Customer only.</summary>
    [HttpPost]
    [Authorize(Roles = Roles.Customer)]
    [ProducesResponseType(typeof(Guid), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> PlaceOrder(
        [FromBody] PlaceOrderCommand command,
        CancellationToken cancellationToken)
    {
        // IN: CustomerId is set inside the handler from JWT claims — not from the body.
        // The command body only carries Items. Security enforced in the handler.
        var orderId = await _mediator.Send(command, cancellationToken);

        return CreatedAtAction(
            nameof(GetOrderById),
            new { id = orderId },
            orderId);
    }

    /// <summary>Cancel an order. Customers can cancel their own. Admins can cancel any.</summary>
    [HttpPut("{id:guid}/cancel")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CancelOrder(
        Guid id,
        [FromBody] CancelOrderRequest request,
        CancellationToken cancellationToken)
    {
        await _mediator.Send(
            new CancelOrderCommand(id, request.Reason),
            cancellationToken);

        return NoContent();
    }

    /// <summary>Update order status. Admin only.</summary>
    [HttpPut("{id:guid}/status")]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateOrderStatus(
        Guid id,
        [FromBody] UpdateOrderStatusCommand command,
        CancellationToken cancellationToken)
    {
        command.OrderId = id;
        await _mediator.Send(command, cancellationToken);
        return NoContent();
    }

    /// <summary>Process payment for an order.</summary>
    [HttpPost("{id:guid}/payment")]
    [ProducesResponseType(typeof(Guid), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ProcessPayment(
        Guid id,
        [FromBody] ProcessPaymentCommand command,
        CancellationToken cancellationToken)
    {
        command.OrderId = id;
        var paymentId = await _mediator.Send(command, cancellationToken);
        return Ok(paymentId);
    }
}

// Inline request DTO for cancel endpoint — reason is optional
// IN: Small request bodies that don't warrant a full Command class
// can be defined as inline records. CancelOrderCommand has a constructor
// that takes these values — the controller maps them.
public record CancelOrderRequest(string? Reason);
