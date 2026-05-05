// RegisterCommandHandler.cs — the thinnest handler in the solution.
// IN: Why is this handler so simple?
// Because AuthService (Identity layer) owns all registration business logic:
// - Password hashing
// - Email uniqueness check
// - UserManager.CreateAsync
// - Role assignment
// - JWT generation
//
// The handler's ONLY job is to be the MediatR integration point:
// receive the command → delegate to IAuthService → return the result.
// This is correct. Handlers orchestrate. Services implement.
//
// IN: Could you skip MediatR for auth and call AuthService from
// the controller directly?
// Yes, technically. But keeping auth commands in MediatR means:
// a) All three pipeline behaviours (logging, validation, exception handling)
//    fire automatically for auth requests — no special handling needed
// b) Auth commands are consistent with all other commands in the system
// c) Easy to add cross-cutting behaviour to auth in the future
// Consistency is a valid architectural reason.

using MediatR;
using NexaStore.Application.Common.Interfaces.Identity;
using NexaStore.Application.Features.Auth.Commands.Login;

namespace NexaStore.Application.Features.Auth.Commands.Register;

public class RegisterCommandHandler
    : IRequestHandler<RegisterCommand, AuthResponseDto>
{
    private readonly IAuthService _authService;

    public RegisterCommandHandler(IAuthService authService)
    {
        _authService = authService;
    }

    public async Task<AuthResponseDto> Handle(
        RegisterCommand command,
        CancellationToken cancellationToken)
    {
        // IN: Handler delegates entirely to IAuthService.
        // No business logic lives here — that belongs in the service.
        // The handler is the MediatR entry point, not the implementation.
        return await _authService.RegisterAsync(command, cancellationToken);
    }
}
