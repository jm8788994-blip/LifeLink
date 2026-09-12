using Microsoft.AspNetCore.Identity;

namespace LifeLink.Services
{
    public class PasswordHasherService : IPasswordHasherService
    {
        private readonly PasswordHasher<string> _hasher = new();

        public string HashPassword(string password)
        {
            if (string.IsNullOrWhiteSpace(password)) return string.Empty;
            return _hasher.HashPassword("LifeLinkUser", password);
        }

        public bool VerifyPassword(string hashedPassword, string providedPassword)
        {
            if (string.IsNullOrEmpty(hashedPassword) || string.IsNullOrEmpty(providedPassword))
                return false;

            // Check if plain text match (for legacy seeded development passwords)
            if (hashedPassword == providedPassword)
                return true;

            try
            {
                var result = _hasher.VerifyHashedPassword("LifeLinkUser", hashedPassword, providedPassword);
                return result == PasswordVerificationResult.Success || result == PasswordVerificationResult.SuccessRehashNeeded;
            }
            catch
            {
                // In case of invalid hash format
                return false;
            }
        }
    }
}
