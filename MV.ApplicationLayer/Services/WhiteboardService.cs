using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MV.ApplicationLayer.Interfaces;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.ApplicationLayer.Services.Agora;
using MV.DomainLayer.Configuration;

namespace MV.ApplicationLayer.Services;

/// <summary>
/// Agora Interactive Whiteboard (Netless): tạo phòng qua REST API + sinh room token.
/// Mỗi buổi học dùng 1 phòng, lưu uuid vào ClassSession.Whiteboardroomuuid.
/// Docs: https://docs.agora.io/en/interactive-whiteboard/reference/whiteboard-api/room-management
/// </summary>
public class WhiteboardService : IWhiteboardService
{
    private const string ApiBase = "https://api.netless.link/v5";
    private const long SdkTokenLifespanMs = 1000L * 60 * 60;         // 1h — đủ để gọi REST tạo phòng
    private const long RoomTokenLifespanMs = 1000L * 60 * 60 * 24;   // 24h — đủ cho buổi học

    private readonly HttpClient _http;
    private readonly WhiteboardSettings _settings;
    private readonly IAppDbContext _context;
    private readonly ILogger<WhiteboardService> _logger;

    public WhiteboardService(
        HttpClient http,
        IOptions<WhiteboardSettings> settings,
        IAppDbContext context,
        ILogger<WhiteboardService> logger)
    {
        _http = http;
        _settings = settings.Value;
        _context = context;
        _logger = logger;
    }

    public async Task<WhiteboardRoomInfo> GetOrCreateRoomAsync(int classSessionId, bool isTutor, CancellationToken ct = default)
    {
        Validate();

        var classSession = await _context.ClassSessions
            .FirstOrDefaultAsync(l => l.Classsessionid == classSessionId, ct)
            ?? throw new InvalidOperationException($"Không tìm thấy buổi học {classSessionId}.");

        var roomUuid = classSession.Whiteboardroomuuid;
        if (string.IsNullOrEmpty(roomUuid))
        {
            roomUuid = await CreateRoomAsync(ct);
            classSession.Whiteboardroomuuid = roomUuid;
            await _context.SaveChangesAsync(ct);
            _logger.LogInformation("Created whiteboard room {RoomUuid} for classSession {ClassSessionId}",
                roomUuid, classSessionId);
        }

        // Room token sinh cục bộ (không cần REST). Tutor = admin, học viên/phụ huynh = writer (đều vẽ được).
        var role = isTutor ? NetlessTokenBuilder.RoleAdmin : NetlessTokenBuilder.RoleWriter;
        var roomToken = NetlessTokenBuilder.RoomToken(
            _settings.AccessKey, _settings.SecretKey, RoomTokenLifespanMs, role, roomUuid);

        return new WhiteboardRoomInfo(
            AppIdentifier: _settings.AppIdentifier,
            Region: _settings.Region,
            RoomUuid: roomUuid,
            RoomToken: roomToken,
            Role: role);
    }

    /// <summary>Tạo phòng mới trên Netless qua REST API, trả về room uuid.</summary>
    private async Task<string> CreateRoomAsync(CancellationToken ct)
    {
        var sdkToken = NetlessTokenBuilder.SdkToken(
            _settings.AccessKey, _settings.SecretKey, SdkTokenLifespanMs, NetlessTokenBuilder.RoleAdmin);

        using var req = new HttpRequestMessage(HttpMethod.Post, $"{ApiBase}/rooms")
        {
            Content = new StringContent("{\"isRecord\":false}", Encoding.UTF8, "application/json")
        };
        req.Headers.TryAddWithoutValidation("token", sdkToken);
        req.Headers.TryAddWithoutValidation("region", _settings.Region);

        using var resp = await _http.SendAsync(req, ct);
        var json = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            _logger.LogError("Netless create room lỗi {Status}: {Body}", (int)resp.StatusCode, json);
            throw new InvalidOperationException($"Tạo phòng whiteboard thất bại ({(int)resp.StatusCode}): {json}");
        }

        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("uuid", out var uuidEl) || uuidEl.GetString() is not { } uuid)
            throw new InvalidOperationException($"Netless không trả về uuid: {json}");
        return uuid;
    }

    private void Validate()
    {
        if (string.IsNullOrWhiteSpace(_settings.AppIdentifier)
            || string.IsNullOrWhiteSpace(_settings.AccessKey)
            || string.IsNullOrWhiteSpace(_settings.SecretKey))
            throw new InvalidOperationException("Whiteboard chưa cấu hình AppIdentifier/AccessKey/SecretKey.");
    }
}
