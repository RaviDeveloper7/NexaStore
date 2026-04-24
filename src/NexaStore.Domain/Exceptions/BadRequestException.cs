// BadRequestException.cs — thrown for invalid business operations.
// INTERVIEW: BadRequestException → 400 Bad Request via ExceptionMiddleware.
// Separate from FluentValidation exceptions (which are also 400) —
// BadRequestException is for BUSINESS RULE violations caught inside handlers,
// e.g. "Cannot cancel a Delivered order."
// FluentValidation is for INPUT validation, e.g. "Name cannot be empty."

namespace NexaStore.Domain.Exceptions;

public class BadRequestException : Exception
{
    public BadRequestException(string message)
        : base(message)
    {
    }
}
