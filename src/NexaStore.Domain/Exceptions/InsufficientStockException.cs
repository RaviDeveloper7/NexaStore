// InsufficientStockException.cs — thrown when an order requests more stock
// than is currently available for a product.
// INTERVIEW: This gets its own exception class (not just BadRequestException)
// because it carries structured data — ProductId, requested, available.
// The ExceptionMiddleware can log these fields to Application Insights
// as custom properties, making stock issues easy to diagnose in production.

namespace NexaStore.Domain.Exceptions;

public class InsufficientStockException : Exception
{
    public Guid ProductId { get; }
    public int RequestedQuantity { get; }
    public int AvailableQuantity { get; }

    public InsufficientStockException(Guid productId, int requested, int available)
        : base($"Insufficient stock for product {productId}. " +
               $"Requested: {requested}, Available: {available}.")
    {
        ProductId = productId;
        RequestedQuantity = requested;
        AvailableQuantity = available;
    }
}
