// LoginCommandValidator.cs — minimal validation for login.
// IN: Login validation is intentionally minimal — just format checks.
// We do NOT validate password complexity here (no Matches() rules).
// Why? A login attempt with a "weak" password is not invalid input —
// it is a valid attempt that will simply fail authentication.
// Blocking it at the validator would tell an attacker:
// "this account exists AND its password does NOT match our complexity rules"
// — information that aids enumeration attacks.
// Validate only what is structurally required: email format, non-empty password.

using FluentValidation;

namespace NexaStore.Application.Features.Auth.Commands.Login;

public class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    public LoginCommandValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty()
                .WithMessage("Email is required.")
            .EmailAddress()
                .WithMessage("A valid email address is required.");

        RuleFor(x => x.Password)
            .NotEmpty()
                .WithMessage("Password is required.");
        // IN: NotEmpty only. No length, no complexity.
        // The authentication service validates the actual password — not us.
    }
}
