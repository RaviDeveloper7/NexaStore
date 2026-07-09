using System.Diagnostics;
using MediatR;
using Microsoft.Extensions.Logging;
using NexaStore.Application.Common.Interfaces.Identity;

namespace NexaStore.Application.Common.Behaviours;

public class LoggingBehaviour<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
{
    private readonly ILogger<LoggingBehaviour<TRequest, TResponse>> _logger;
    private readonly ICurrentUserService _currentUserService;

    public LoggingBehaviour(ILogger<LoggingBehaviour<TRequest, TResponse>> logger, ICurrentUserService currentUserService)
    {
        _logger = logger;
        _currentUserService = currentUserService;
    }

    public async Task<TResponse> Handle(TRequest request,
        RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;
        var userId = _currentUserService.UserId ?? "Anonymous";

        _logger.LogInformation(
            "NexaStore Request: {RequestName} | UserId: {UserId} | Request: {@Request}",
            requestName,
            userId,
            request);

        var stopwatch = Stopwatch.StartNew();

        TResponse response;

        try
        {
            response = await next();
        }
        finally
        {
            stopwatch.Stop();

            var elapsed = stopwatch.ElapsedMilliseconds;

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
