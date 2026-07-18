using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.ApplicationLayer.Services.Agora;
using MV.DomainLayer.Configuration;
using System.Security.Cryptography;

namespace MV.ApplicationLayer.Services;

/// <summary>
/// Sinh Agora RTC token (AccessToken2 — version "007") để client join video call.
///
/// Thiết kế:
///   - Channel dùng chung theo booking: <c>booking-{bookingId}</c> (deterministic, không
///     gọi API ngoài). Mọi buổi của cùng một booking chia sẻ một phòng — "một meet link".
///   - Agora user account = UserId của người dùng → client map remote user ↔ app user
///     trực tiếp, đồng bộ với dictionary participantNames trả về từ controller.
///   - Role mặc định = Publisher (tutor / student / parent đều publish audio + video).
/// </summary>
public class AgoraRTCService(
    IOptions<AgoraSettings> settings,
    ILogger<AgoraRTCService> logger) : IAgoraRTCService
{
    private readonly AgoraSettings _settings = settings.Value;

    /// <inheritdoc/>
    public string GenerateToken(string channelName, string account)
    {
        if (string.IsNullOrWhiteSpace(_settings.AppId) || string.IsNullOrWhiteSpace(_settings.AppCertificate))
            throw new InvalidOperationException("Agora chưa được cấu hình (thiếu AppId hoặc AppCertificate).");

        if (string.IsNullOrWhiteSpace(channelName))
            throw new ArgumentException("channelName không được rỗng.", nameof(channelName));

        var issueTs = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var expire = (uint)_settings.TokenExpireSeconds;
        var salt = GenerateSalt();

        var token = RtcTokenBuilder2.BuildTokenWithUserAccount(
            _settings.AppId,
            _settings.AppCertificate,
            channelName,
            account,
            RtcTokenBuilder2.RolePublisher,
            tokenExpire: expire,
            privilegeExpire: expire,
            issueTs: issueTs,
            salt: salt);

        logger.LogDebug("Generated Agora token for channel={Channel}, account={Account}, expire={Expire}s",
            channelName, account, expire);
        return token;
    }

    /// <inheritdoc/>
    public AgoraRoomInfo GetRoomInfo(int classSessionId, int? bookingId, string userId)
    {
        // Channel dùng chung theo booking. Fallback theo classSessionId nếu buổi chưa gắn booking.
        var channelName = bookingId.HasValue ? $"booking-{bookingId.Value}" : classSessionId.ToString();
        var token = GenerateToken(channelName, userId);
        var expireAt = (int)(DateTimeOffset.UtcNow.ToUnixTimeSeconds() + _settings.TokenExpireSeconds);

        return new AgoraRoomInfo(
            Channel: channelName,
            ClassSessionId: classSessionId,
            Uid: userId,
            Token: token,
            AppId: _settings.AppId,
            ExpireAt: expireAt);
    }

    /// <summary>Sinh salt ngẫu nhiên (uint32, khác 0) cho AccessToken2.</summary>
    private static uint GenerateSalt()
    {
        Span<byte> bytes = stackalloc byte[4];
        RandomNumberGenerator.Fill(bytes);
        var salt = BitConverter.ToUInt32(bytes);
        return salt == 0 ? 1u : salt;
    }
}
