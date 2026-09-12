using System.Net;
using System.Net.Mail;

namespace LifeLink.Services
{
    public class EmailSenderService : IEmailSenderService
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<EmailSenderService> _logger;

        public EmailSenderService(IConfiguration configuration, ILogger<EmailSenderService> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<bool> SendEmailAsync(string toEmail, string subject, string htmlBody)
        {
            var host = _configuration["Smtp:Host"];
            var portStr = _configuration["Smtp:Port"];
            var user = _configuration["Smtp:User"];
            var pass = _configuration["Smtp:Pass"];
            var from = _configuration["Smtp:From"] ?? "noreply@lifelink.com";

            // If SMTP is configured, attempt real transmission
            if (!string.IsNullOrWhiteSpace(host) && int.TryParse(portStr, out int port) && !string.IsNullOrWhiteSpace(user))
            {
                try
                {
                    using var client = new SmtpClient(host, port)
                    {
                        Credentials = new NetworkCredential(user, pass),
                        EnableSsl = true,
                        Timeout = 10000
                    };

                    using var message = new MailMessage(from, toEmail, subject, htmlBody)
                    {
                        IsBodyHtml = true
                    };

                    await client.SendMailAsync(message);
                    _logger.LogInformation("Email sent successfully to {ToEmail}", toEmail);
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to send live email to {ToEmail}. Operating in simulated/local preview mode.", toEmail);
                }
            }
            else
            {
                _logger.LogInformation("SMTP not configured. Generated in-app email preview for {ToEmail}", toEmail);
            }

            return false;
        }
    }
}
