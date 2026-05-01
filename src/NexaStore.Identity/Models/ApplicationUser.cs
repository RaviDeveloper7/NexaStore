// ApplicationUser.cs — extends ASP.NET Core Identity's IdentityUser.
// INTERVIEW: Why extend IdentityUser instead of using it directly?
// IdentityUser gives you Email, PasswordHash, PhoneNumber, LockoutEnabled etc.
// We extend it to add domain-specific fields: FirstName, LastName, RefreshToken.
// This keeps all user data in one table — no JOIN needed to get the user's name.
//
// INTERVIEW: Why is RefreshToken on ApplicationUser and not in a separate table?
// For NexaStore we allow ONE active refresh token per user (single device).
// If you need multi-device support, move refresh tokens to their own table
// with a FK back to ApplicationUser — one user → many refresh tokens.

using Microsoft.AspNetCore.Identity;

namespace NexaStore.Identity.Models;

public class ApplicationUser : IdentityUser
{
    // INTERVIEW: IdentityUser already has an Id (string, not Guid).
    // ASP.NET Core Identity uses string IDs by default to support multiple
    // key types. We keep string here for full Identity compatibility —
    // changing it to Guid requires overriding the entire Identity stack.

    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;

    // Computed — not stored. Used for display and email templates.
    // INTERVIEW: NotMapped means EF Core ignores this property — no column created.
    // Computed from persisted FirstName + LastName — no sync issues.
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public string FullName => $"{FirstName} {LastName}".Trim();

    // --- Refresh Token fields ---

    // The refresh token value — stored as a hash in production.
    // INTERVIEW: Storing the raw token is acceptable for a portfolio project.
    // In production you hash it (SHA-256) before storing, same as passwords.
    // This way even if the DB is compromised, stolen tokens can't be replayed.
    public string? RefreshToken { get; set; }

    // When this refresh token expires — checked on every refresh request.
    // After expiry the user must log in again with username/password.
    public DateTime? RefreshTokenExpiryTime { get; set; }

    // INTERVIEW: IsActive flag for soft-disabling users.
    // Identity has IsLockedOut but that's time-based.
    // IsActive = false means permanently disabled until an admin re-enables them.
    public bool IsActive { get; set; } = true;
}
