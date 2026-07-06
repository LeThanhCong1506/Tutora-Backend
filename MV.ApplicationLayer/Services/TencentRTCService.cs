using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Configuration;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MV.ApplicationLayer.Services;

/// <summary>
/// Tạo UserSig theo thuật toán chính thức của Tencent RTC (TLS HMAC-SHA256).
/// 
/// Thuật toán (https://trtc.io/document/35166):
///   1. Tạo chuỗi plaintext theo định dạng TLS
///   2. Ký bằng HMAC-SHA256 với SecretKey
///   3. Đóng gói JSON
///   4. Nén bằng zlib (Deflate)
///   5. Base64 encode với ký tự thay thế: +→*, /→-, =→_
/// </summary>
public class TencentRTCService(
    IOptions<TencentRTCSettings> settings,
    ILogger<TencentRTCService> logger) : ITencentRTCService
{
    private readonly TencentRTCSettings _settings = settings.Value;

    /// <inheritdoc/>
    public string GenerateUserSig(string userId)
    {
        try
        {
            var currentTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var expireTime = _settings.TokenExpireSeconds;

            // Bước 1: Tạo chuỗi cần ký theo định dạng Tencent TLS
            var plaintext = BuildPlaintext(userId, currentTime, expireTime);

            // Bước 2: Ký HMAC-SHA256
            var sig = ComputeHmacSha256(_settings.SecretKey, plaintext);

            // Bước 3: Tạo JSON payload
            var payload = new
            {
                TLSver = "2.0",
                TLSidentifier = userId,
                TLSsdkappid = _settings.SdkAppId,
                TLSexpire = expireTime,
                TLStime = currentTime,
                TLSsig = sig
            };

            // Serialize JSON với tên field đúng format Tencent
            var json = SerializeTlsPayload(userId, _settings.SdkAppId, currentTime, expireTime, sig);

            // Bước 4: Nén zlib (Deflate - không có header zlib, giống cách Tencent SDK làm)
            var compressed = CompressWithZlib(json);

            // Bước 5: Base64 encode với ký tự đặc biệt của Tencent
            var userSig = TencentBase64Encode(compressed);

            logger.LogDebug("Generated UserSig for userId={UserId}, expire={Expire}s", userId, expireTime);
            return userSig;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to generate UserSig for userId={UserId}", userId);
            throw;
        }
    }

    /// <inheritdoc/>
    public TRTCRoomInfo GetRoomInfo(int classSessionId, string userId)
    {
        var userSig = GenerateUserSig(userId);
        var expireAt = (int)(DateTimeOffset.UtcNow.ToUnixTimeSeconds() + _settings.TokenExpireSeconds);

        return new TRTCRoomInfo(
            RoomId: classSessionId,
            UserId: userId,
            UserSig: userSig,
            SdkAppId: _settings.SdkAppId,
            ExpireAt: expireAt
        );
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Tạo chuỗi plaintext theo định dạng TLS của Tencent.
    /// Format: "TLS.identifier:<userId>\nTLS.sdkappid:<sdkAppId>\nTLS.time:<time>\nTLS.expire:<expire>\n"
    /// </summary>
    private string BuildPlaintext(string userId, long currentTime, int expireTime)
    {
        return $"TLS.identifier:{userId}\n" +
               $"TLS.sdkappid:{_settings.SdkAppId}\n" +
               $"TLS.time:{currentTime}\n" +
               $"TLS.expire:{expireTime}\n";
    }

    /// <summary>
    /// Tính HMAC-SHA256 và trả về Base64 string.
    /// </summary>
    private static string ComputeHmacSha256(string key, string data)
    {
        var keyBytes = Encoding.UTF8.GetBytes(key);
        var dataBytes = Encoding.UTF8.GetBytes(data);

        using var hmac = new HMACSHA256(keyBytes);
        var hash = hmac.ComputeHash(dataBytes);
        return Convert.ToBase64String(hash);
    }

    /// <summary>
    /// Serialize JSON payload đúng tên field của Tencent TLS.
    /// Dùng thủ công thay vì JsonSerializer vì cần tên field cố định (không theo convention C#).
    /// </summary>
    private static string SerializeTlsPayload(string userId, int sdkAppId, long currentTime, int expireTime, string sig)
    {
        // Tencent yêu cầu format JSON với tên field chính xác
        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream);

        writer.WriteStartObject();
        writer.WriteString("TLS.ver", "2.0");
        writer.WriteString("TLS.identifier", userId);
        writer.WriteNumber("TLS.sdkappid", sdkAppId);
        writer.WriteNumber("TLS.expire", expireTime);
        writer.WriteNumber("TLS.time", currentTime);
        writer.WriteString("TLS.sig", sig);
        writer.WriteEndObject();

        writer.Flush();
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>
    /// Nén dữ liệu bằng zlib Deflate (không có zlib wrapper - raw deflate).
    /// Tencent SDK sử dụng raw deflate, không phải GZip.
    /// </summary>
    private static byte[] CompressWithZlib(string data)
    {
        var inputBytes = Encoding.UTF8.GetBytes(data);

        using var outputStream = new MemoryStream();

        // Tencent dùng zlib deflate: 2 byte header (0x78 0x9C) + deflate data + 4 byte Adler-32 checksum
        // Dùng ZLibStream của .NET 6+ (bao gồm zlib header và checksum tự động)
        using (var zlibStream = new ZLibStream(outputStream, CompressionMode.Compress, leaveOpen: true))
        {
            zlibStream.Write(inputBytes, 0, inputBytes.Length);
        }

        return outputStream.ToArray();
    }

    /// <summary>
    /// Base64 encode theo quy ước của Tencent:
    ///   '+' → '*'
    ///   '/' → '-'
    ///   '=' → '_'
    /// </summary>
    private static string TencentBase64Encode(byte[] data)
    {
        return Convert.ToBase64String(data)
            .Replace('+', '*')
            .Replace('/', '-')
            .Replace('=', '_');
    }
}
