using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Exceptions;
using MV.DomainLayer.Helpers;

namespace MV.ApplicationLayer.Services
{
    /// <summary>
    /// Xem <see cref="ILargeTransactionOtpService"/>. Cùng pattern Redis OTP entry với
    /// <c>SimpleAuthService</c> (cố ý không tái dùng trực tiếp — service đó gắn với
    /// xác thực đăng nhập, sửa vào sẽ rủi ro cho luồng auth đang chạy).
    /// </summary>
    public class LargeTransactionOtpService : ILargeTransactionOtpService
    {
        private readonly IDistributedCache _cache;
        private readonly IOtpSender _otpSender;
        private readonly ILogger<LargeTransactionOtpService> _logger;

        private const int OtpExpiryMinutes = 5;
        private const int MaxOtpAttempts = 5;
        private const int OtpResendCooldownSeconds = 60;
        private const int MaxOtpSendsPerDay = 5;
        private const int ApprovedTtlMinutes = 15;

        public LargeTransactionOtpService(
            IDistributedCache cache,
            IOtpSender otpSender,
            ILogger<LargeTransactionOtpService> logger)
        {
            _cache = cache;
            _otpSender = otpSender;
            _logger = logger;
        }

        public async Task SendAsync(int bookingId, string phase, string parentPhone)
        {
            var key = OtpKey(bookingId, phase);

            var cooldownLeft = await GetCooldownSecondsLeftAsync(key);
            if (cooldownLeft > 0)
                throw new OtpCooldownActiveException(cooldownLeft);

            var dailyEntry = await GetDailyLimitAsync(key);
            if (dailyEntry.Count >= MaxOtpSendsPerDay)
                throw new OtpDailyLimitExceededException();

            var code = GenerateOtpCode();
            await StoreOtpAsync(key, code);
            await _otpSender.SendOtpAsync(parentPhone, code);
            await MarkSentAsync(key, dailyEntry);

            _logger.LogInformation(
                "Sent large-transaction OTP for booking {BookingId} phase {Phase}", bookingId, phase);
        }

        public async Task VerifyAsync(int bookingId, string phase, string code)
        {
            var key = OtpKey(bookingId, phase);
            var entry = await GetOtpAsync(key)
                ?? throw new OtpExpiredException();

            if (entry.ExpiresAtUtc <= TimeZoneHelper.UtcNow)
            {
                await RemoveOtpAsync(key);
                throw new OtpExpiredException();
            }

            if (entry.Attempts >= MaxOtpAttempts)
            {
                await RemoveOtpAsync(key);
                throw new OtpTooManyAttemptsException();
            }

            if (entry.Code != code.Trim())
            {
                entry.Attempts++;
                await SaveOtpAsync(key, entry);
                throw new OtpInvalidException();
            }

            await RemoveOtpAsync(key);
            await _cache.SetStringAsync(ApprovedKey(bookingId, phase), "1",
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(ApprovedTtlMinutes) });

            _logger.LogInformation(
                "Verified large-transaction OTP for booking {BookingId} phase {Phase}", bookingId, phase);
        }

        public async Task<bool> IsApprovedAsync(int bookingId, string phase)
        {
            var raw = await _cache.GetStringAsync(ApprovedKey(bookingId, phase));
            return !string.IsNullOrEmpty(raw);
        }

        // ─── Key namespace: otp:largetx:{bookingId}:{phase}, tách khỏi otp:phone:*/otp:pwdreset:* ───
        private static string OtpKey(int bookingId, string phase) => $"otp:largetx:{bookingId}:{phase}";
        private static string ApprovedKey(int bookingId, string phase) => $"otp:largetx:approved:{bookingId}:{phase}";
        private static string CooldownKey(string otpKey) => $"{otpKey}:cooldown";
        private static string DailyLimitKey(string otpKey) => $"{otpKey}:daily";

        private static string GenerateOtpCode()
            => RandomNumberGenerator.GetInt32(100000, 1000000).ToString();

        private sealed class OtpEntry
        {
            public string Code { get; set; } = "";
            public int Attempts { get; set; }
            public DateTime ExpiresAtUtc { get; set; }
        }

        private Task StoreOtpAsync(string key, string code)
        {
            var entry = new OtpEntry
            {
                Code = code,
                Attempts = 0,
                ExpiresAtUtc = TimeZoneHelper.UtcNow.AddMinutes(OtpExpiryMinutes)
            };
            return SaveOtpAsync(key, entry);
        }

        // Re-save với CÙNG absolute expiry để lần nhập sai không kéo dài tuổi thọ OTP.
        private Task SaveOtpAsync(string key, OtpEntry entry)
        {
            var json = JsonSerializer.Serialize(entry);
            var ttl = entry.ExpiresAtUtc - TimeZoneHelper.UtcNow;
            if (ttl <= TimeSpan.Zero) ttl = TimeSpan.FromSeconds(1);
            return _cache.SetStringAsync(key, json, new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = ttl
            });
        }

        private async Task<OtpEntry?> GetOtpAsync(string key)
        {
            var json = await _cache.GetStringAsync(key);
            return string.IsNullOrEmpty(json) ? null : JsonSerializer.Deserialize<OtpEntry>(json);
        }

        private Task RemoveOtpAsync(string key) => _cache.RemoveAsync(key);

        private async Task<int> GetCooldownSecondsLeftAsync(string otpKey)
        {
            var raw = await _cache.GetStringAsync(CooldownKey(otpKey));
            if (string.IsNullOrEmpty(raw)
                || !DateTime.TryParse(raw, null, System.Globalization.DateTimeStyles.RoundtripKind, out var sentAtUtc))
                return 0;

            var secondsLeft = OtpResendCooldownSeconds - (TimeZoneHelper.UtcNow - sentAtUtc).TotalSeconds;
            return secondsLeft > 0 ? (int)Math.Ceiling(secondsLeft) : 0;
        }

        private sealed class DailyLimitEntry
        {
            public int Count { get; set; }
            public DateTime WindowStartUtc { get; set; }
        }

        private async Task<DailyLimitEntry> GetDailyLimitAsync(string otpKey)
        {
            var raw = await _cache.GetStringAsync(DailyLimitKey(otpKey));
            if (!string.IsNullOrEmpty(raw))
            {
                var entry = JsonSerializer.Deserialize<DailyLimitEntry>(raw)!;
                if (TimeZoneHelper.UtcNow - entry.WindowStartUtc < TimeSpan.FromHours(24))
                    return entry;
            }
            return new DailyLimitEntry { Count = 0, WindowStartUtc = TimeZoneHelper.UtcNow };
        }

        private Task SaveDailyLimitAsync(string otpKey, DailyLimitEntry entry)
        {
            var remaining = TimeSpan.FromHours(24) - (TimeZoneHelper.UtcNow - entry.WindowStartUtc);
            if (remaining <= TimeSpan.Zero) remaining = TimeSpan.FromSeconds(1);
            return _cache.SetStringAsync(DailyLimitKey(otpKey), JsonSerializer.Serialize(entry),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = remaining });
        }

        private async Task MarkSentAsync(string otpKey, DailyLimitEntry dailyEntry)
        {
            await _cache.SetStringAsync(
                CooldownKey(otpKey),
                TimeZoneHelper.UtcNow.ToString("o"),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(OtpResendCooldownSeconds) });

            dailyEntry.Count++;
            await SaveDailyLimitAsync(otpKey, dailyEntry);
        }
    }
}
