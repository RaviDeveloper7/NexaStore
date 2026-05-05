// RefreshTokenCommandHandler.cs — delegates to IAuthService.RefreshTokenAsync.
// Thin handler — all the logic (validate expired JWT, check stored token,
// rotation) lives in AuthService (Week 3 Day 3).
// The handler is the pipeline entry point only.

using MediatR;
using NexaStore.Application.Common.Interfaces.Identity;
using NexaStore.Application.Features.Auth.Commands.Login;

namespace NexaStore.Application.Features.Auth.Commands.RefreshToken;

public class RefreshTokenCommandHandler
    : IRequestHandler<RefreshTokenCommand, AuthResponseDto>
{
    private readonly IAuthService _authService;

    public RefreshTokenCommandHandler(IAuthService authService)
    {
        _authService = authService;
    }

    public async Task<AuthResponseDto> Handle(
        RefreshTokenCommand command,
        CancellationToken cancellationToken)
    {
        return await _authService.RefreshTokenAsync(command, cancellationToken);
    }
}
