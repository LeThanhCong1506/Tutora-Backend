using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Configuration;
using Resend;

namespace MV.ApplicationLayer.Services;

/// <summary>
/// Implementation of <see cref="IEmailService"/> sử dụng Resend API.
/// Thay thế hoàn toàn SmtpEmailService cũ.
/// </summary>
public class ResendEmailService : IEmailService
{
    private readonly ResendSettings _settings;
    private readonly IResend _resend;
    private readonly ILogger<ResendEmailService> _logger;

    public ResendEmailService(
        IOptions<ResendSettings> options,
        IResend resend,
        ILogger<ResendEmailService> logger)
    {
        _settings = options.Value;
        _resend = resend;
        _logger = logger;
    }

    public async Task SendEmailAsync(string to, string subject, string body, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(to))
            throw new ArgumentException("Recipient email is required.", nameof(to));

        var message = new EmailMessage
        {
            From = $"{_settings.SenderName} <{_settings.SenderEmail}>",
            Subject = subject,
            HtmlBody = body
        };
        message.To.Add(to);

        _logger.LogInformation("Sending email via Resend to {Email}", to);

        try
        {
            var response = await _resend.EmailSendAsync(message, cancellationToken);
            _logger.LogInformation("Email sent successfully via Resend. EmailId: {EmailId}", response.Content);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send email via Resend. Ignoring for local testing.");
        }
    }
}
