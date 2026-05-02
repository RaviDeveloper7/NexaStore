// ValidationBehaviour.cs — runs all FluentValidation validators for the request
// BEFORE the handler executes. If any rule fails, throws ValidationException
// immediately — the handler is NEVER called.

using FluentValidation;
using MediatR;
using NexaStore.Application.Common.Exceptions;
using ValidationException = NexaStore.Application.Common.Exceptions.ValidationException;

namespace NexaStore.Application.Common.Behaviours;

public class ValidationBehaviour<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    
    private readonly IEnumerable<IValidator<TRequest>> _validators;

    public ValidationBehaviour(IEnumerable<IValidator<TRequest>> validators)
    {
        _validators = validators;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        // Fast path — if no validators are registered for this request type,
        // skip the entire validation block and go straight to the handler.

        if (!_validators.Any())
            return await next();

        // Build a FluentValidation context for the request
        var context = new ValidationContext<TRequest>(request);


        var validationResults = await Task.WhenAll(
            _validators.Select(v => v.ValidateAsync(context, cancellationToken)));

        // Flatten all failures from all validators into one list
        var failures = validationResults
            .Where(r => r.Errors.Count > 0)
            .SelectMany(r => r.Errors)
            .ToList();

        if (failures.Count > 0)
        {
            throw new ValidationException(failures);
        }

        // All validators passed — proceed to the next behaviour / handler
        return await next();
    }
}
