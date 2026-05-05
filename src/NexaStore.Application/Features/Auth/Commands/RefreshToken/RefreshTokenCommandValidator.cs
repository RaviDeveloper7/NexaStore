// RefreshTokenCommandValidator.cs — structural validation only.
// Both tokens must be present — if either is missing, reject immediately.
// Content validation (is the JWT signature valid? does the refresh token match?)
// is done inside AuthService — requires cryptographic operations and DB access.

using FluentValidation;

namespace NexaStore.Application.Features.Auth.Commands.RefreshToken;

public class RefreshTokenCommandValidator : AbstractValidator<RefreshTokenCommand>
{
    public RefreshTokenCommandValidator()
    {
        RuleFor(x => x.AccessToken)
            .NotEmpty()
                .WithMessage("Access token is required.");

        RuleFor(x => x.RefreshToken)
            .NotEmpty()
                .WithMessage("Refresh token is required.");
    }
}
