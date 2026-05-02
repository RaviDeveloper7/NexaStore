// LoggingBehaviour.cs — logs every MediatR request with timing and user context.
using System.Diagnostics;
using MediatR;
using Microsoft.Extensions.Logging;
using NexaStore.Application.Common.Interfaces.Identity;

namespace NexaStore.Application.Common.Behaviours;
public class LoggingBehaviour<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly ILogger<LoggingBehaviour<TRequest, TResponse>> _logger;
    private readonly ICurrentUserService _currentUserService;

    public LoggingBehaviour(
        ILogger<LoggingBehaviour<TRequest, TResponse>> logger,
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
        // Extract the short class name for readable log messages
        // e.g. "PlaceOrderCommand" not the full namespace
        var requestName = typeof(TRequest).Name;
        var userId = _currentUserService.UserId ?? "Anonymous";

        // --- PRE-HANDLER LOG ---
        _logger.LogInformation(
            "NexaStore Request: {RequestName} | UserId: {UserId} | Request: {@Request}",
            requestName,
            userId,
            request);

        // Start a high-resolution timer — Stopwatch is more accurate than DateTime.UtcNow diff

        var stopwatch = Stopwatch.StartNew();

        TResponse response;

        try
        {
            // next() calls the next behaviour in the pipeline, eventually reaching the handler
            response = await next();
        }
        finally
        {
            stopwatch.Stop();

            var elapsed = stopwatch.ElapsedMilliseconds;

            // --- POST-HANDLER LOG ---
            if (elapsed > 500)
            {
                _logger.LogWarning(
                    "NexaStore SLOW Request: {RequestName} | UserId: {UserId} | " +
                    "Duration: {ElapsedMs}ms — consider optimising this operation.",
                    requestName,
                    userId,
                    elapsed);
            }
            else
            {
                _logger.LogInformation(
                    "NexaStore Response: {RequestName} | UserId: {UserId} | Duration: {ElapsedMs}ms",
                    requestName,
                    userId,
                    elapsed);
            }
        }

        return response;
    }
}
