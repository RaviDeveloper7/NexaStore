// AuthController.cs — handles Register, Login, and RefreshToken.
// IN: Controllers are intentionally thin — they:
// 1. Receive HTTP input
// 2. Build the command/query
// 3. Send via MediatR
// 4. Return the result as HTTP response
// Zero business logic lives here. All logic is in handlers and services.
// A controller is just an HTTP adapter for MediatR.

using Asp.Versioning;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using NexaStore.Application.Features.Auth.Commands.Login;
using NexaStore.Application.Features.Auth.Commands.RefreshToken;
using NexaStore.Application.Features.Auth.Commands.Register;

namespace NexaStore.Api.Controllers.v1;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/auth")]

// IN: No [Authorize] attribute on AuthController — these endpoints are public.
// Register and Login are the entry points before a token exists.
// RefreshToken accepts an expired JWT — the JWT middleware would reject it
// if [Authorize] were present.
public class AuthController : ControllerBase
{
    private readonly IMediator _mediator;

    public AuthController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>Register a new customer account.</summary>
    /// <returns>JWT access token and refresh token.</returns>
    [HttpPost("register")]
    [ProducesResponseType(typeof(AuthResponseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Register([FromBody] RegisterCommand command,CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);

        // IN: 201 Created for successful registration — not 200 OK.
        // A new resource (user account) was created. 201 is semantically correct.
        // CreatedAtAction points to the resource — for auth, there's no "get user by id"
        // endpoint so we return the token payload directly.
        return StatusCode(StatusCodes.Status201Created, result);
    }

    /// <summary>Login with email and password.</summary>
    /// <returns>JWT access token and refresh token.</returns>
    [HttpPost("login")]
    [ProducesResponseType(typeof(AuthResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login([FromBody] LoginCommand command,CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);
        return Ok(result);
    }

    /// <summary>Exchange a refresh token for a new token pair.</summary>
    /// <returns>New JWT access token and new refresh token.</returns>
    [HttpPost("refresh-token")]
    [ProducesResponseType(typeof(AuthResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> RefreshToken([FromBody] RefreshTokenCommand command,CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);
        return Ok(result);
    }
}
