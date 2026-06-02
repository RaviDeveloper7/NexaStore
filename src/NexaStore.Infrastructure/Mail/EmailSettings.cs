// EmailSettings.cs — strongly-typed email provider configuration.
// IN: NexaStore uses SendGrid as the email provider — industry standard,
// generous free tier, excellent deliverability.
// The interface (IEmailService) is provider-agnostic — swap SendGrid for
// AWS SES or Mailgun by replacing this settings class and EmailService only.
// No handler changes required.

namespace NexaStore.Infrastructure.Mail;

public class EmailSettings
{
    public const string SectionName = "EmailSettings";

    // SendGrid API key — never commit this to source control.
    // Store in Azure Key Vault (prod) or User Secrets (dev).
    // IN: API key auth over SMTP credentials — more secure, easier to rotate.
    // SMTP requires username + password. API key is a single revocable token.
    public string ApiKey { get; set; } = string.Empty;

    // The "From" email address — must be verified in SendGrid
    // IN: SendGrid requires domain verification to prevent spoofing.
    // noreply@ is the standard for transactional emails the customer
    // should not reply to. Use support@ if replies are expected.
    public string FromEmail { get; set; } = "noreply@nexastore.com";

    // Display name shown in the email client
    public string FromName { get; set; } = "NexaStore";
}
