// ICurrentUserService.cs — exposes the authenticated user's context to handlers.
// INTERVIEW: Handlers should never touch HttpContext directly — that's an
// infrastructure concern. ICurrentUserService abstracts the claims extraction
// so handlers stay testable. In unit tests, mock this to return any user you need.

namespace NexaStore.Application.Common.Interfaces.Identity;

public interface ICurrentUserService
{
    // The authenticated user's ID — extracted from the JWT "sub" claim.
    // Null if the request is unauthenticated.
    string? UserId { get; }

    // The user's role — "Admin" or "Customer"
    // INTERVIEW: Used in handlers like GetOrdersQueryHandler to decide
    // whether to return all orders or just this customer's orders.
    string? Role { get; }

    // Helper shortcut — avoids null check + string compare in every handler
    bool IsAdmin { get; }

    // True when a valid JWT is present and the user is authenticated
    bool IsAuthenticated { get; }
}
