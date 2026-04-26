// AuthResponseDto.cs — returned by Register, Login, and RefreshToken.
// INTERVIEW: Single response DTO for all auth operations — consistent contract.
// The client always gets a JWT + refresh token + expiry in the same shape.

namespace NexaStore.Application.Features.Auth.Commands.Login;

public class AuthResponseDto
{
    // The JWT access token — short-lived (15–60 minutes)
    public string AccessToken { get; set; } = string.Empty;

    // Refresh token — long-lived (7–30 days), stored securely by client
    // INTERVIEW: Refresh tokens must be stored server-side (hashed) so they
    // can be revoked. A JWT alone cannot be revoked before expiry.
    public string RefreshToken { get; set; } = string.Empty;

    // UTC expiry of the access token — lets client proactively refresh
    public DateTime AccessTokenExpiry { get; set; }
}
