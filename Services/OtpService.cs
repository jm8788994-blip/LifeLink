using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace LifeLink.Services
{
    public class OtpService : IOtpService
    {
        private class OtpRecord
        {
            public string Code { get; set; } = string.Empty;
            public DateTime ExpiresAt { get; set; }
            public int Attempts { get; set; }
        }

        // Key: $"{purpose}:{email.ToLowerInvariant()}"
        private readonly ConcurrentDictionary<string, OtpRecord> _cache = new();

        public string GenerateOtp(string email, string purpose)
        {
            if (string.IsNullOrWhiteSpace(email)) return string.Empty;

            // Generate cryptographically secure 6-digit number
            int number = RandomNumberGenerator.GetInt32(100000, 1000000);
            string code = number.ToString("D6");

            var key = GetKey(email, purpose);
            _cache[key] = new OtpRecord
            {
                Code = code,
                ExpiresAt = DateTime.UtcNow.AddMinutes(5),
                Attempts = 0
            };

            return code;
        }

        public bool ValidateOtp(string email, string code, string purpose)
        {
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(code)) return false;

            var key = GetKey(email, purpose);
            if (!_cache.TryGetValue(key, out var record))
            {
                return false;
            }

            if (DateTime.UtcNow > record.ExpiresAt)
            {
                _cache.TryRemove(key, out _);
                return false;
            }

            record.Attempts++;
            if (record.Attempts > 4)
            {
                _cache.TryRemove(key, out _);
                return false;
            }

            if (record.Code.Trim() == code.Trim())
            {
                _cache.TryRemove(key, out _);
                return true;
            }

            return false;
        }

        public string? GetLatestOtp(string email, string purpose)
        {
            if (string.IsNullOrWhiteSpace(email)) return null;

            var key = GetKey(email, purpose);
            if (_cache.TryGetValue(key, out var record) && DateTime.UtcNow <= record.ExpiresAt)
            {
                return record.Code;
            }
            return null;
        }

        public void ClearOtp(string email, string purpose)
        {
            var key = GetKey(email, purpose);
            _cache.TryRemove(key, out _);
        }

        private static string GetKey(string email, string purpose) => $"{purpose.Trim().ToLowerInvariant()}:{email.Trim().ToLowerInvariant()}";
    }
}
