// LoginCommand.cs — full implementation replacing the Week 1 stub.
// IN: Login only needs Email and Password.
// Device information, IP address, user-agent — these are captured in middleware
// or enriched by Application Insights automatically.
// Keep the command lean — it carries only what the authentication flow needs.

using MediatR;

namespace NexaStore.Application.Features.Auth.Commands.Login;

public class LoginCommand : IRequest<AuthResponseDto>
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}
