// UnhandledExceptionBehaviour.cs — catches unexpected exceptions from handlers,
// logs them with full context, then rethrows so ExceptionMiddleware can
// map them to the correct HTTP response.

using MediatR;
using Microsoft.Extensions.Logging;
using NexaStore.Application.Common.Interfaces.Identity;
using NexaStore.Domain.Exceptions;

namespace NexaStore.Application.Common.Behaviours;

public class UnhandledExceptionBehaviour<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly ILogger<UnhandledExceptionBehaviour<TRequest, TResponse>> _logger;
    private readonly ICurrentUserService _currentUserService;

    public UnhandledExceptionBehaviour(
        ILogger<UnhandledExceptionBehaviour<TRequest, TResponse>> logger,
        ICurrentUserService currentUserService)
    {
        _logger = logger;
        _currentUserService = currentUserService;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        try
        {
            return await next();
        }

        // --- Known domain exceptions — log as Warning, not Error ---
       
        catch (NotFoundException ex)
        {
            _logger.LogWarning(
                ex,
                "NexaStore Not Found: {RequestName} | UserId: {UserId} | Message: {Message}",
                typeof(TRequest).Name,
                _currentUserService.UserId ?? "Anonymous",
                ex.Message);

            // Rethrow — ExceptionMiddleware maps this to 404
            throw;
        }
        catch (BadRequestException ex)
        {
            _logger.LogWarning(
                ex,
                "NexaStore Bad Request: {RequestName} | UserId: {UserId} | Message: {Message}",
                typeof(TRequest).Name,
                _currentUserService.UserId ?? "Anonymous",
                ex.Message);

            // Rethrow — ExceptionMiddleware maps this to 400
            throw;
        }
        catch (InsufficientStockException ex)
        {

            _logger.LogWarning(
                ex,
                "NexaStore Insufficient Stock: {RequestName} | UserId: {UserId} | " +
                "ProductId: {ProductId} | Requested: {Requested} | Available: {Available}",
                typeof(TRequest).Name,
                _currentUserService.UserId ?? "Anonymous",
                ex.ProductId,
                ex.RequestedQuantity,
                ex.AvailableQuantity);

            // Rethrow — ExceptionMiddleware maps this to 400
            throw;
        }

        // --- Unknown exceptions — log as Error with full context ---
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "NexaStore Unhandled Exception: {RequestName} | UserId: {UserId} | " +
                "Request: {@Request}",
                typeof(TRequest).Name,
                _currentUserService.UserId ?? "Anonymous",
                request);

            throw;
        }
    }
}
