// IAuthService.cs — contract for authentication operations.
// INTERVIEW: Defined in Application, implemented in Identity layer.
// Application handlers (RegisterCommandHandler, LoginCommandHandler) depend on
// this interface — they never reference ASP.NET Core Identity directly.
// This means you could swap Identity for Keycloak by just replacing the implementation.

using NexaStore.Application.Features.Auth.Commands.Login;
using NexaStore.Application.Features.Auth.Commands.RefreshToken;
using NexaStore.Application.Features.Auth.Commands.Register;

namespace NexaStore.Application.Common.Interfaces.Identity;

public interface IAuthService
{
    // Register a new user — creates Identity user + assigns Customer role
    // Returns the AuthResponseDto with JWT + refresh token on success
    Task<AuthResponseDto> RegisterAsync(
        RegisterCommand request,
        CancellationToken cancellationToken = default);

    // Authenticate a user and issue JWT + refresh token
    Task<AuthResponseDto> LoginAsync(
        LoginCommand request,
        CancellationToken cancellationToken = default);

    // Validate expired JWT + valid refresh token → issue new JWT + refresh token
    // INTERVIEW: Refresh token rotation — old refresh token is invalidated on use.
    // Prevents replay attacks. If a stolen token is used, the legitimate user's
    // next refresh will fail, alerting them to the compromise.
    Task<AuthResponseDto> RefreshTokenAsync(
        RefreshTokenCommand request,
        CancellationToken cancellationToken = default);
}
