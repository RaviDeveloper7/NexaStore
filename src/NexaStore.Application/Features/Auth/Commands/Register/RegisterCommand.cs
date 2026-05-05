// RegisterCommand.cs — full implementation replacing the Week 1 stub.
// IN: RegisterCommand is an IRequest<AuthResponseDto> — registration
// immediately returns a token pair. No separate login step required after sign-up.
// This is the UX-correct approach — don't make the user log in right after registering.
//
// IN: Why does RegisterCommand live in Application if the actual
// registration logic is in AuthService (Identity layer)?
// The command is the APPLICATION-LEVEL representation of the intent.
// The handler (in Application) receives the command and delegates to IAuthService.
// IAuthService is defined in Application.Common.Interfaces — the boundary.
// The Identity layer implements IAuthService. Application never references Identity directly.

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
