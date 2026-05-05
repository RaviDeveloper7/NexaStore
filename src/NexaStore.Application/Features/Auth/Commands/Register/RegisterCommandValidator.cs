// RegisterCommandValidator.cs — input validation for registration.
// IN: Validates structure and format — not business rules.
// "Is the email a valid format?" is validation (here).
// "Is the email already registered?" is a business rule (AuthService).
// The validator runs before AuthService is ever called — no DB hit for bad input.

using FluentValidation;

namespace NexaStore.Application.Features.Auth.Commands.Register;

public class RegisterCommandValidator : AbstractValidator<RegisterCommand>
{
    public RegisterCommandValidator()
    {
        // --- FirstName ---
        RuleFor(x => x.FirstName)
            .NotEmpty()
                .WithMessage("First name is required.")
            .MaximumLength(100)
                .WithMessage("First name must not exceed 100 characters.")
            .Matches(@"^[a-zA-Z\s\-']+$")
                .WithMessage("First name contains invalid characters.");

        // --- LastName ---
        RuleFor(x => x.LastName)
            .NotEmpty()
                .WithMessage("Last name is required.")
            .MaximumLength(100)
                .WithMessage("Last name must not exceed 100 characters.")
            .Matches(@"^[a-zA-Z\s\-']+$")
                .WithMessage("Last name contains invalid characters.");

        // --- Email ---
        RuleFor(x => x.Email)
            .NotEmpty()
                .WithMessage("Email is required.")
            .EmailAddress()
                .WithMessage("A valid email address is required.")
            .MaximumLength(256)
                .WithMessage("Email must not exceed 256 characters.");
        // IN: FluentValidation's .EmailAddress() uses a regex that matches
        // RFC 5322 format. It is NOT a guarantee the email exists — that requires
        // sending a verification email. It only checks format validity.

        // --- Password ---
        // IN: Validate password complexity rules here to give immediate,
        // field-level error messages. Identity also enforces these rules in
        // CreateAsync — but Identity returns generic error codes ("PasswordTooShort"),
        // not the clean user-facing messages the validator produces.
        // Two layers of enforcement — cleaner error messages win.
        RuleFor(x => x.Password)
            .NotEmpty()
                .WithMessage("Password is required.")
            .MinimumLength(8)
                .WithMessage("Password must be at least 8 characters.")
            .MaximumLength(100)
                .WithMessage("Password must not exceed 100 characters.")
            .Matches(@"[A-Z]")
                .WithMessage("Password must contain at least one uppercase letter.")
            .Matches(@"[a-z]")
                .WithMessage("Password must contain at least one lowercase letter.")
            .Matches(@"[0-9]")
                .WithMessage("Password must contain at least one digit.");
        // IN: Matches() uses regex — each rule is a separate Matches() call
        // so each produces a specific error message.
        // A single regex combining all rules would produce one generic message —
        // "Password is invalid" — which is useless to the user.
    }
}
