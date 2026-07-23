using System;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;

namespace MV.ApplicationLayer.Services;

/// <summary>
/// Phát hành + xác thực token ngắn hạn cho phép stream MỘT bản ghi buổi học.
/// Payload "{classSessionId}:{userId}:{expUnixSeconds}" ký HMAC-SHA256, không lưu DB (stateless).
/// Khóa ký tách biệt với Jwt:Key bằng cách băm thêm hậu tố cố định — tránh dùng chung 1 khóa cho
/// hai mục đích ký khác nhau mà không cần thêm cấu hình mới.
/// </summary>
public class RecordingAccessTokenService : IRecordingAccessTokenService
{
    private readonly byte[] _signingKey;

    public RecordingAccessTokenService(IConfiguration configuration)
    {
        var jwtKey = configuration[ConfigurationKeys.Jwt.Key];
        if (string.IsNullOrWhiteSpace(jwtKey))
            throw new InvalidOperationException("Jwt:Key is required to sign recording access tokens.");

        _signingKey = SHA256.HashData(Encoding.UTF8.GetBytes(jwtKey + ":recording-access-token"));
    }

    public string Issue(int classSessionId, string userId, TimeSpan lifetime)
    {
        var exp = DateTimeOffset.UtcNow.Add(lifetime).ToUnixTimeSeconds();
        var payload = $"{classSessionId}:{userId}:{exp}";
        var signature = Sign(payload);

        return $"{UrlSafeBase64Encode(Encoding.UTF8.GetBytes(payload))}.{UrlSafeBase64Encode(signature)}";
    }

    public bool TryValidate(string token, int classSessionId, out string? userId)
    {
        userId = null;
        if (string.IsNullOrWhiteSpace(token)) return false;

        var parts = token.Split('.', 2);
        if (parts.Length != 2) return false;

        string payload;
        byte[] receivedSignature;
        try
        {
            payload = Encoding.UTF8.GetString(UrlSafeBase64Decode(parts[0]));
            receivedSignature = UrlSafeBase64Decode(parts[1]);
        }
        catch (FormatException)
        {
            return false;
        }

        var expectedSignature = Sign(payload);
        if (receivedSignature.Length != expectedSignature.Length
            || !CryptographicOperations.FixedTimeEquals(receivedSignature, expectedSignature))
            return false;

        var segments = payload.Split(':');
        if (segments.Length != 3) return false;
        if (!int.TryParse(segments[0], out var tokenClassSessionId) || tokenClassSessionId != classSessionId) return false;
        if (!long.TryParse(segments[2], out var expUnix)) return false;
        if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() > expUnix) return false;

        userId = segments[1];
        return true;
    }

    private byte[] Sign(string payload)
    {
        using var hmac = new HMACSHA256(_signingKey);
        return hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
    }

    private static string UrlSafeBase64Encode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] UrlSafeBase64Decode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        switch (padded.Length % 4)
        {
            case 2: padded += "=="; break;
            case 3: padded += "="; break;
        }
        return Convert.FromBase64String(padded);
    }
}
