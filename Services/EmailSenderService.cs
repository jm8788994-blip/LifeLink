using System.Net;
using System.Net.Http.Headers;
using System.Net.Mail;
using System.Text;
using System.Text.Json;

namespace LifeLink.Services
{
    public class EmailSenderService : IEmailSenderService
    {
        private static readonly HttpClient _httpClient = new();

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

            // 1) Prefer a configured SMTP server (e.g. Gmail) - guarantees real delivery
            if (!string.IsNullOrWhiteSpace(host) && int.TryParse(portStr, out int port) && !string.IsNullOrWhiteSpace(user))
            {
                try
                {
                    using var client = new SmtpClient(host, port)
                    {
                        Credentials = new NetworkCredential(user, pass ?? string.Empty),
                        EnableSsl = true,
                        Timeout = 15000
                    };

                    using (var message = new MailMessage(user, toEmail, subject, htmlBody)
                    {
                        IsBodyHtml = true
                    })
                    {
                        await client.SendMailAsync(message);
                    }

                    _logger.LogInformation("Email sent successfully to {ToEmail} via SMTP ({Host})", toEmail, host);
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "SMTP send failed to {ToEmail}. Trying Mailtrap fallback.", toEmail);
                }
            }

            var token = _configuration["Mailtrap:Token"];
            var fromEmail = _configuration["Mailtrap:FromEmail"] ?? "hello@demomailtrap.co";
            var fromName = _configuration["Mailtrap:FromName"] ?? "LifeLink";

            // 2) Fallback: Mailtrap REST API (no extra package needed)
            if (!string.IsNullOrWhiteSpace(token) && token != "YOUR_MAILTRAP_API_TOKEN")
            {
                try
                {
                    var payload = new
                    {
                        from = new { email = fromEmail, name = fromName },
                        to = new[] { new { email = toEmail } },
                        subject = subject,
                        html = htmlBody,
                        category = "LifeLink"
                    };

                    using var request = new HttpRequestMessage(HttpMethod.Post, "https://send.api.mailtrap.io/api/send")
                    {
                        Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
                    };
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

                    using var response = await _httpClient.SendAsync(request);
                    if (response.IsSuccessStatusCode)
                    {
                        _logger.LogInformation("Email sent successfully to {ToEmail} via Mailtrap", toEmail);
                        return true;
                    }

                    _logger.LogWarning("Mailtrap send failed ({Code}) for {ToEmail}. Falling back to local preview mode.", (int)response.StatusCode, toEmail);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Mailtrap exception while sending to {ToEmail}. Operating in simulated/local preview mode.", toEmail);
                }
            }
            else
            {
                _logger.LogInformation("SMTP & Mailtrap not configured. Generated in-app email preview for {ToEmail}", toEmail);
            }

            return false;
        }
    }
}
