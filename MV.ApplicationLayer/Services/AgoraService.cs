using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Configuration;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace MV.ApplicationLayer.Services;

/// <summary>
/// Tạo Agora AccessToken2 (version "007") theo thuật toán chính thức của Agora.
///
/// Thuật toán (https://docs.agora.io/en/video-calling/get-started/authentication-workflow):
///   1. Pack nội dung: AppId + IssueTs + Expire + Salt + Services (RTC privileges)
///   2. Ký bằng HMAC-SHA256 với AppCertificate
///   3. Ghép nội dung + chữ ký
///   4. Nén bằng zlib
///   5. Base64 encode, prepend "007"
/// </summary>
public class AgoraService(
    IOptions<AgoraSettings> settings,
    ILogger<AgoraService> logger) : IAgoraService
{
    // RTC Service type
    private const ushort ServiceTypeRtc = 1;

    // RTC privilege IDs
    private const ushort PrivJoinChannel       = 1;
    private const ushort PrivPublishAudioStream = 2;
    private const ushort PrivPublishVideoStream  = 3;
    private const ushort PrivPublishDataStream   = 4;

    private readonly AgoraSettings _settings = settings.Value;

    /// <inheritdoc/>
    public string GenerateToken(string channelName, string userId)
    {
        try
        {
            var issueTs = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var expire  = (uint)_settings.TokenExpireSeconds;
            var salt    = (uint)Random.Shared.Next(1, int.MaxValue);

            var privileges = new Dictionary<ushort, uint>
            {
                [PrivJoinChannel]        = expire,
                [PrivPublishAudioStream] = expire,
                [PrivPublishVideoStream] = expire,
                [PrivPublishDataStream]  = expire,
            };

            // Bước 1: Pack nội dung
            var content   = PackContent(_settings.AppId, issueTs, expire, salt, channelName, userId, privileges);

            // Bước 2: Ký HMAC-SHA256
            var signature = ComputeHmac(_settings.AppCertificate, content);

            // Bước 3: Ghép nội dung + chữ ký
            var payload   = AppendSignature(content, signature);

            // Bước 4-5: Nén + Base64
            var compressed = ZlibCompress(payload);
            var token      = "007" + Convert.ToBase64String(compressed);

            logger.LogDebug("Generated Agora token for channel={Channel}, userId={UserId}, expire={Expire}s",
                channelName, userId, expire);
            return token;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to generate Agora token for channel={Channel}, userId={UserId}",
                channelName, userId);
            throw;
        }
    }

    /// <inheritdoc/>
    public AgoraRoomInfo GetRoomInfo(int lessonId, string userId)
    {
        var channelName = lessonId.ToString();
        var token       = GenerateToken(channelName, userId);
        var expireAt    = (int)(DateTimeOffset.UtcNow.ToUnixTimeSeconds() + _settings.TokenExpireSeconds);

        return new AgoraRoomInfo(
            ChannelName: channelName,
            UserId:      userId,
            Token:       token,
            AppId:       _settings.AppId,
            ExpireAt:    expireAt
        );
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Pack toàn bộ nội dung token theo định dạng Agora AccessToken2.
    /// Tất cả integer dùng little-endian.
    /// </summary>
    private static byte[] PackContent(
        string appId, uint issueTs, uint expire, uint salt,
        string channelName, string uid,
        Dictionary<ushort, uint> privileges)
    {
        using var ms     = new MemoryStream();
        using var writer = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true);

        // Header
        WriteString(writer, appId);
        writer.Write(issueTs);
        writer.Write(expire);
        writer.Write(salt);

        // 1 service (RTC)
        writer.Write((ushort)1);

        // RTC service type
        writer.Write(ServiceTypeRtc);

        // Channel name + UID
        WriteString(writer, channelName);
        WriteString(writer, uid);

        // Privileges (sorted by key for determinism)
        writer.Write((ushort)privileges.Count);
        foreach (var priv in privileges.OrderBy(p => p.Key))
        {
            writer.Write(priv.Key);
            writer.Write(priv.Value);
        }

        writer.Flush();
        return ms.ToArray();
    }

    /// <summary>
    /// Ghép nội dung + chữ ký (length-prefixed) thành byte array cuối cùng.
    /// </summary>
    private static byte[] AppendSignature(byte[] content, byte[] signature)
    {
        using var ms     = new MemoryStream(content.Length + 2 + signature.Length);
        using var writer = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true);

        writer.Write(content);
        WriteString(writer, signature);  // packString(signature bytes)

        writer.Flush();
        return ms.ToArray();
    }

    /// <summary>
    /// HMAC-SHA256(key=appCertificate, data=contentBytes) → raw bytes.
    /// </summary>
    private static byte[] ComputeHmac(string appCertificate, byte[] content)
    {
        var keyBytes = Encoding.UTF8.GetBytes(appCertificate);
        using var hmac = new HMACSHA256(keyBytes);
        return hmac.ComputeHash(content);
    }

    /// <summary>
    /// Nén bằng zlib (ZLibStream của .NET 6+ — bao gồm header 0x78 0x9C + Adler-32).
    /// </summary>
    private static byte[] ZlibCompress(byte[] data)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionMode.Compress, leaveOpen: true))
        {
            zlib.Write(data, 0, data.Length);
        }
        return output.ToArray();
    }

    /// <summary>
    /// Ghi string (hoặc byte[]) với 2-byte length prefix (little-endian uint16).
    /// </summary>
    private static void WriteString(BinaryWriter w, string s)
    {
        var bytes = Encoding.UTF8.GetBytes(s);
        w.Write((ushort)bytes.Length);
        w.Write(bytes);
    }

    private static void WriteString(BinaryWriter w, byte[] bytes)
    {
        w.Write((ushort)bytes.Length);
        w.Write(bytes);
    }
}
