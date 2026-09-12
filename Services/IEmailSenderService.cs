namespace LifeLink.Services
{
    public interface IEmailSenderService
    {
        Task<bool> SendEmailAsync(string toEmail, string subject, string htmlBody);
    }
}
