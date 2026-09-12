namespace LifeLink.Services
{
    public interface IOtpService
    {
        string GenerateOtp(string email, string purpose);
        bool ValidateOtp(string email, string code, string purpose);
        string? GetLatestOtp(string email, string purpose);
        void ClearOtp(string email, string purpose);
    }
}
