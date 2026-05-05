// LoginCommandHandler.cs — delegates to IAuthService.LoginAsync.
// Same pattern as RegisterCommandHandler — thin handler, rich service.
// IN: The MediatR pipeline benefits apply here too:
// LoggingBehaviour logs every login attempt with timing.
// UnhandledExceptionBehaviour catches and logs failed login exceptions.
// No explicit try/catch needed in the handler — the pipeline handles it.

using MediatR;
using NexaStore.Application.Common.Interfaces.Identity;

namespace NexaStore.Application.Features.Auth.Commands.Login;

public class LoginCommandHandler
    : IRequestHandler<LoginCommand, AuthResponseDto>
{
    private readonly IAuthService _authService;

    public LoginCommandHandler(IAuthService authService)
    {
        _authService = authService;
    }

    public async Task<AuthResponseDto> Handle(
        LoginCommand command,
        CancellationToken cancellationToken)
    {
        return await _authService.LoginAsync(command, cancellationToken);
    }
}
