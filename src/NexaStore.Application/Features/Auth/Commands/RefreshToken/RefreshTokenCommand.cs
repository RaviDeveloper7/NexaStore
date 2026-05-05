// RefreshTokenCommand.cs — full implementation replacing the Week 1 stub.
// IN: The client sends BOTH the expired access token AND the refresh token.
// Why both? The access token identifies WHO the user is (via JWT claims).
// The refresh token proves the caller is the legitimate owner of that identity.
// Neither alone is sufficient — see AuthService.RefreshTokenAsync for full explanation.

using MediatR;
using NexaStore.Application.Features.Auth.Commands.Login;

namespace NexaStore.Application.Features.Auth.Commands.RefreshToken;

public class RefreshTokenCommand : IRequest<AuthResponseDto>
{
    // The expired (or nearly expired) JWT — still cryptographically valid
    public string AccessToken { get; set; } = string.Empty;

    // The long-lived refresh token — must match what's stored on ApplicationUser
    public string RefreshToken { get; set; } = string.Empty;
}
