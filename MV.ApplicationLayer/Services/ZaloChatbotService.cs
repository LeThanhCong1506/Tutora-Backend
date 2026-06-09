using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel.Zalo;
using StackExchange.Redis;
using System.Text;
using System.Text.Json;

namespace MV.ApplicationLayer.Services;

public class ZaloChatbotService : IZaloChatbotService
{
    private readonly IZaloOAService _oaService;
    private readonly ITutorSearchService _tutorSearchService;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<ZaloChatbotService> _logger;
    private readonly string _miniAppId;

    public ZaloChatbotService(
        IZaloOAService oaService,
        ITutorSearchService tutorSearchService,
        IConnectionMultiplexer redis,
        ILogger<ZaloChatbotService> logger,
        IConfiguration configuration)
    {
        _oaService = oaService;
        _tutorSearchService = tutorSearchService;
        _redis = redis;
        _logger = logger;
        _miniAppId = configuration[ConfigurationKeys.ZaloOA.AppId] ?? string.Empty;
    }

    public async Task HandleUserMessageAsync(string senderZaloId, string messageText)
    {
        var db = _redis.GetDatabase();
        var sessionKey = $"zalo:chat:{senderZaloId}";
        var sessionJson = await db.StringGetAsync(sessionKey);
        var session = sessionJson.HasValue
            ? JsonSerializer.Deserialize<ChatSession>(sessionJson!) ?? new ChatSession()
            : new ChatSession();

        var text = messageText.Trim();

        // Quick reply payloads và intent detection
        if (text == ZaloChatbotIntent.FindTutor || ContainsAny(text, "tìm", "gia sư", "tìm gia sư", "cần gia sư"))
        {
            session.State = ZaloChatbotState.AskSubject;
            await SaveSession(db, sessionKey, session);
            await _oaService.SendOAMessageAsync(senderZaloId,
                "Bạn cần gia sư môn gì?\n\nVí dụ: Toán, Lý, Hóa, Anh Văn, Tin Học...");
            return;
        }

        if (text == ZaloChatbotIntent.ViewCalendar || ContainsAny(text, "lịch", "lịch học", "buổi học", "calendar"))
        {
            await _oaService.SendOAMessageAsync(senderZaloId,
                $"Xem lịch học tại đây:\nhttps://zalo.me/app/link/{_miniAppId}?path=/parent-portal/calendar");
            return;
        }

        if (text == ZaloChatbotIntent.Contact || ContainsAny(text, "liên hệ", "hỗ trợ", "support", "help"))
        {
            await _oaService.SendOAMessageAsync(senderZaloId,
                "Liên hệ hỗ trợ:\nEmail: support@tutora.vn\nWeb: https://tutora.vn");
            return;
        }

        // State machine
        switch (session.State)
        {
            case ZaloChatbotState.AskSubject:
                session.Subject = text;
                session.State = ZaloChatbotState.AskGrade;
                await SaveSession(db, sessionKey, session);
                await _oaService.SendOAMessageAsync(senderZaloId,
                    $"Môn: {session.Subject}\n\nCho lớp mấy? (VD: 10, 11, 12, Đại học)");
                break;

            case ZaloChatbotState.AskGrade:
                session.Grade = text;
                session.State = ZaloChatbotState.AskArea;
                await SaveSession(db, sessionKey, session);
                await _oaService.SendOAMessageAsync(senderZaloId,
                    $"Môn: {session.Subject} — Lớp: {session.Grade}\n\nBạn ở khu vực nào? (VD: Quận 7, Thủ Đức, Bình Thạnh)");
                break;

            case ZaloChatbotState.AskArea:
                session.Area = text;
                session.State = ZaloChatbotState.Idle;
                await db.KeyDeleteAsync(sessionKey);
                await SearchAndReplyAsync(senderZaloId, session);
                break;

            default:
                await _oaService.SendOAMessageWithButtonsAsync(senderZaloId,
                    "Tôi có thể giúp gì cho bạn?",
                    new List<ZaloQuickReply>
                    {
                        new() { Title = "Tìm gia sư",   Payload = ZaloChatbotIntent.FindTutor },
                        new() { Title = "Xem lịch học", Payload = ZaloChatbotIntent.ViewCalendar },
                        new() { Title = "Liên hệ",      Payload = ZaloChatbotIntent.Contact }
                    });
                break;
        }
    }

    private async Task SearchAndReplyAsync(string senderZaloId, ChatSession session)
    {
        try
        {
            var results = await _tutorSearchService.SearchTutorsAsync(new TutorSearchParameters
            {
                SearchTerm = session.Subject,
                TeachingAreaDistrict = session.Area,
                PageNumber = 1,
                PageSize = 3,
                SortBy = TutorSearchSortBy.Default
            });

            if (results.Items.Count == 0)
            {
                await _oaService.SendOAMessageAsync(senderZaloId,
                    $"Không tìm thấy gia sư {session.Subject} lớp {session.Grade} tại {session.Area}.\n\n" +
                    "Bạn có thể thử lại với tiêu chí khác.");
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Tìm thấy {results.Items.Count} gia sư {session.Subject} phù hợp:\n");
            for (int i = 0; i < results.Items.Count; i++)
            {
                var t = results.Items[i];
                var rating = t.AverageRating.HasValue ? $"{t.AverageRating:F1}" : "Mới";
                var subjects = t.Subjects != null && t.Subjects.Any()
                    ? string.Join(", ", t.Subjects.Select(s => s.SubjectName).Distinct().Take(2))
                    : "Nhiều môn";
                sb.AppendLine($"{i + 1}. {t.FullName} - {rating} sao — {subjects}");
            }
            sb.AppendLine($"\nXem chi tiết: https://zalo.me/app/link/{_miniAppId}?path=/tutor-search");

            await _oaService.SendOAMessageAsync(senderZaloId, sb.ToString());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Chatbot search failed for {ZaloId}", senderZaloId);
            await _oaService.SendOAMessageAsync(senderZaloId,
                "Xin lỗi, có lỗi xảy ra khi tìm kiếm. Vui lòng thử lại sau.");
        }
    }

    private static bool ContainsAny(string text, params string[] keywords)
        => keywords.Any(k => text.Contains(k, StringComparison.OrdinalIgnoreCase));

    private static async Task SaveSession(IDatabase db, string key, ChatSession session)
    {
        session.UpdatedAt = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;
        await db.StringSetAsync(key, JsonSerializer.Serialize(session), TimeSpan.FromMinutes(10));
    }
}

public class ChatSession
{
        public string State { get; set; } = ZaloChatbotState.Idle;
    public string? Subject { get; set; }
    public string? Grade { get; set; }
    public string? Area { get; set; }
    public DateTime UpdatedAt { get; set; } = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;
}
