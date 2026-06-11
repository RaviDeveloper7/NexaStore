//this is dummy email controller to check if sendgrid is working or not. This will be removed in future and email sending will be done by Azure function which will consume messages from service bus topic and send email using IEmailService implementation (EmailService.cs) which uses SendGrid as email provider.
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SendGrid;
using SendGrid.Helpers.Mail;

namespace NexaStore.Infrastructure.Mail.Controller;

[ApiController]
//[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/email")]
public class EmailController : ControllerBase
{
    private readonly EmailSettings _settings;
    private readonly ILogger<EmailService> _logger;

    public EmailController(
        IOptions<EmailSettings> settings,
        ILogger<EmailService> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }
    /// <summary>
    /// Get paged orders. Customers see their own orders. Admins see all orders.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> SendEmailAsync(string toEmail,  string body, string plainContent, CancellationToken cancellationToken)
    {
        var client = new SendGridClient(_settings.ApiKey);

        var from = new EmailAddress(_settings.FromEmail, _settings.FromName);
        var to = new EmailAddress("some sendgrid registerd mail");
        var subject = "SendGrid Test Email";
        var plainTextContent = "Hello, this is a test email from NexaStore!";
        var htmlContent = "<strong>Hello, this is a test email from NexaStore!</strong>";

        var msg = MailHelper.CreateSingleEmail(from, to, subject, plainTextContent, htmlContent);
        var response = await client.SendEmailAsync(msg);

        return Ok();
    }
}
