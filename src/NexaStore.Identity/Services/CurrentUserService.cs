// CurrentUserService.cs — extracts the authenticated user's identity from
// the current HTTP request's ClaimsPrincipal.
//
// INTERVIEW: Why abstract this behind ICurrentUserService?
// Handlers should NEVER touch HttpContext directly. Reasons:
// 1. Clean Architecture — HttpContext is an infrastructure concern.
//    Application layer has zero knowledge of HTTP.
// 2. Testability — in unit tests, mock ICurrentUserService to return any
//    user you need. No need to construct a fake HttpContext + ClaimsPrincipal.
// 3. Single Responsibility — claim extraction logic lives in one place.
//    If the claim key changes (e.g. "sub" → "uid"), you fix it here — done.
//
// INTERVIEW: How does CurrentUserService know who is making the request?
// ASP.NET Core's JWT middleware (configured in IdentityServiceRegistration)
// validates the JWT on every request and populates HttpContext.User with
// a ClaimsPrincipal containing all the claims from the token.
// CurrentUserService reads from that ClaimsPrincipal — zero DB calls.

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using NexaStore.Application.Common.Interfaces.Identity;
using NexaStore.Identity.Settings;

namespace NexaStore.Identity.Services;

public class CurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    // INTERVIEW: IHttpContextAccessor is the ONLY way to access HttpContext
    // outside of a controller or middleware. It is registered as Singleton by
    // ASP.NET Core (AddHttpContextAccessor()) and stores a per-request reference
    // to the current HttpContext using AsyncLocal<T> — thread-safe by design.
    // We inject it here rather than HttpContext directly because HttpContext
    // cannot be injected into Scoped services safely (its lifetime doesn't match).
    public CurrentUserService(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    // The ClaimsPrincipal for the current request — null if no HTTP context
    // (e.g. background job, Azure Function, test).
    // Private helper to avoid repeating the null-check everywhere.
    private ClaimsPrincipal? User =>
        _httpContextAccessor.HttpContext?.User;

    public string? UserId
    {
        get
        {
            // INTERVIEW: We check two claims for the user ID:
            // 1. JwtRegisteredClaimNames.Sub ("sub") — OIDC standard subject claim
            // 2. "uid" — our custom claim added in GenerateJwtTokenAsync
            // Both contain the same value (IdentityUser.Id).
            // We check sub first (standards-compliant), fall back to uid.
            // This makes CurrentUserService resilient if the JWT structure changes.
            return User?.FindFirstValue(JwtRegisteredClaimNames.Sub)
                ?? User?.FindFirstValue("uid");
        }
    }

    public string? Role
    {
        get
        {
            // INTERVIEW: ClaimTypes.Role is the .NET-specific role claim key.
            // ASP.NET Core Identity writes roles using this claim type,
            // and [Authorize(Roles = "Admin")] reads from this claim type.
            // It maps to the JWT claim "http://schemas.microsoft.com/ws/2008/06/identity/claims/role"
            // — verbose but standard. The JWT middleware maps it automatically.
            return User?.FindFirstValue(ClaimTypes.Role);
        }
    }

    public bool IsAdmin =>
        // INTERVIEW: Role string comparison is case-sensitive here.
        // Use the Roles constant — not a magic string — to guarantee consistency.
        // If IsAdmin is called in a background job context (no HTTP request),
        // User is null → Role is null → IsAdmin is false. Correct — a background
        // job should never be considered an admin user.
        string.Equals(Role, Roles.Admin, StringComparison.Ordinal);

    public bool IsAuthenticated =>
        // INTERVIEW: Identity.IsAuthenticated is set by the JWT middleware
        // after successful token validation. It is false for:
        // - Requests with no Authorization header
        // - Requests with an invalid/expired JWT
        // - Background jobs with no HTTP context
        // Handlers check this before accessing UserId to avoid null reference issues.
        User?.Identity?.IsAuthenticated ?? false;
}
