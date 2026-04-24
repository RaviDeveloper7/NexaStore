// NotFoundException.cs — thrown when a requested resource does not exist.
// INTERVIEW: Custom domain exceptions are caught by the global ExceptionMiddleware
// in the API layer and mapped to the correct HTTP status codes.
// NotFoundException → 404 Not Found
// This means handlers never deal with HTTP — they just throw domain exceptions.

namespace NexaStore.Domain.Exceptions;

public class NotFoundException : Exception
{
    // INTERVIEW: The message format "EntityName (Id: xxx) was not found" is
    // consistent across the entire API — makes debugging in Application Insights
    // much faster because you can search by entity name or ID.
    public NotFoundException(string name, object key)
        : base($"{name} (Id: {key}) was not found.")
    {
    }
}
