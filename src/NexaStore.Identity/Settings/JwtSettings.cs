// JwtSettings.cs — strongly-typed configuration for JWT.
// INTERVIEW: Options Pattern is the .NET standard for configuration binding.
// Compared to reading IConfiguration["JwtSettings:Key"] directly:
// - Compile-time safety — no magic strings
// - Validated at startup via IOptions<T>
// - Testable — just new up JwtSettings in unit tests
// - All JWT config in one place — easy to audit

namespace NexaStore.Identity.Settings;

public class JwtSettings
{
    // appsettings.json section name — used in IdentityServiceRegistration
    public const string SectionName = "JwtSettings";

    // The signing secret — minimum 32 characters (256 bits) for HmacSha256
    // INTERVIEW: This key must be kept secret — it is the only thing preventing
    // token forgery. Store it in Azure Key Vault in production,
    // User Secrets in development. Never in appsettings.json committed to git.
    public string Key { get; set; } = string.Empty;

    // The token issuer — identifies who created the token
    // Validated on every request against TokenValidationParameters.ValidIssuer
    public string Issuer { get; set; } = string.Empty;

    // The intended audience — identifies who the token is for
    // Validated on every request against TokenValidationParameters.ValidAudience
    public string Audience { get; set; } = string.Empty;

    // How long the JWT access token is valid — short-lived by design
    // INTERVIEW: Short expiry (15-60 min) limits the damage if a token is stolen.
    // A stolen JWT cannot be revoked before expiry (stateless = no server state).
    // Short expiry + refresh token rotation is the industry standard mitigation.
    public int DurationInMinutes { get; set; } = 60;

    // How long the refresh token is valid — long-lived, stored server-side
    // INTERVIEW: Refresh tokens CAN be revoked (they're stored in the DB).
    // If a refresh token is compromised, clear it from ApplicationUser — done.
    // The attacker's next refresh attempt returns 401.
    public int RefreshTokenDurationInDays { get; set; } = 7;
}
