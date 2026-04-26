// Stub — full implementation in Week 4 Day 5.
using MediatR;

namespace NexaStore.Application.Features.Auth.Commands.Login;

public class LoginCommand : IRequest<AuthResponseDto>
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}
