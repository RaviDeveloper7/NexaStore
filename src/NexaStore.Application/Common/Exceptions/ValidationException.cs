// ValidationException.cs — thrown by ValidationBehaviour when FluentValidation fails.
// INTERVIEW: This is Application's own exception — separate from Domain exceptions.
// Domain exceptions = business rule violations (NotFoundException, BadRequestException).
// Application ValidationException = input validation failures (empty name, negative price).
// ExceptionMiddleware maps this → 400 Bad Request with a structured errors dictionary.

using FluentValidation.Results;

namespace NexaStore.Application.Common.Exceptions;

public class ValidationException : Exception
{
    // Dictionary of field name → list of error messages for that field
    // e.g. { "Name": ["Name is required", "Name must be less than 200 chars"] }
    // INTERVIEW: Dictionary format matches the RFC 7807 Problem Details standard
    // used by ASP.NET Core's built-in validation responses — familiar to any client.
    public IDictionary<string, string[]> Errors { get; }

    public ValidationException(IEnumerable<ValidationFailure> failures)
        : base("One or more validation failures have occurred.")
    {
        // Group failures by property name, collect messages per property
        Errors = failures
            .GroupBy(e => e.PropertyName, e => e.ErrorMessage)
            .ToDictionary(
                failureGroup => failureGroup.Key,
                failureGroup => failureGroup.ToArray()
            );
    }
}
