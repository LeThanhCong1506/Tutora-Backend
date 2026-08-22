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
            var candidateUuid = await CreateRoomAsync(ct);

            // Gia sư + học viên thường mở bảng vẽ gần như cùng lúc lúc buổi vừa bắt đầu — 2 request
            // này race nhau đọc Whiteboardroomuuid=null rồi CÙNG tạo phòng Netless riêng, mỗi bên
            // lưu uuid của chính mình → 2 người join 2 phòng khác nhau, không bao giờ thấy nét vẽ
            // của nhau. Chặn bằng update có điều kiện: chỉ request NÀO ghi được (WHERE ...IS NULL)
            // mới thắng; request thua phải đọc lại và dùng ĐÚNG uuid đã thắng, không dùng uuid vừa
            // tự tạo (phòng đó bị bỏ phí nhưng vô hại — không có gì tham chiếu tới).
            var affected = await _context.ClassSessions
                .Where(l => l.Classsessionid == classSessionId && l.Whiteboardroomuuid == null)
                .ExecuteUpdateAsync(s => s.SetProperty(l => l.Whiteboardroomuuid, candidateUuid), ct);

            if (affected == 1)
            {
                roomUuid = candidateUuid;
                _logger.LogInformation("Created whiteboard room {RoomUuid} for classSession {ClassSessionId}",
                    roomUuid, classSessionId);
            }
            else
            {
                roomUuid = await _context.ClassSessions
                    .Where(l => l.Classsessionid == classSessionId)
                    .Select(l => l.Whiteboardroomuuid)
                    .FirstAsync(ct)
                    ?? throw new InvalidOperationException($"Không đọc lại được Whiteboardroomuuid cho buổi {classSessionId}.");
                _logger.LogInformation(
                    "Whiteboard room race for classSession {ClassSessionId}: dùng lại uuid {RoomUuid} từ request khác đã thắng.",
                    classSessionId, roomUuid);
            }
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
