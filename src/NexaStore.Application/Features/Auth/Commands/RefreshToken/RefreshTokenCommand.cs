// Stub — full implementation in Week 4 Day 5.
using MediatR;
using NexaStore.Application.Features.Auth.Commands.Login;

namespace NexaStore.Application.Features.Auth.Commands.RefreshToken;

public class RefreshTokenCommand : IRequest<AuthResponseDto>
{
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
}
