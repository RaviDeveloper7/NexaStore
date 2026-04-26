// Stub — full implementation in Week 4 Day 5.
// Created now so IAuthService compiles.
using MediatR;
using NexaStore.Application.Features.Auth.Commands.Login;

namespace NexaStore.Application.Features.Auth.Commands.Register;

public class RegisterCommand : IRequest<AuthResponseDto>
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}
