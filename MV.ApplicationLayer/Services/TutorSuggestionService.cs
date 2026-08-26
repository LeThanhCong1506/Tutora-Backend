using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using MV.ApplicationLayer.Interfaces;
using MV.ApplicationLayer.RepositoryInterfaces;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.Entities;
using MV.DomainLayer.Helpers;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace MV.ApplicationLayer.Services;

/// <summary>
/// Ghép tín hiệu học tập (chương học sinh hỏi nhiều trong phiên) với danh sách gia sư.
/// </summary>
public class TutorSuggestionService(
    IAppDbContext context,
    IStudentRepository studentRepo,
    IStudentService studentService,
    ITutorRecommendService tutorRecommendService,
    IMemoryCache cache,
    ILogger<TutorSuggestionService> logger) : ITutorSuggestionService
{
    /// <summary>
    /// Bước gọi tutora-ai (vector rank), mà học sinh cuộn cuối chat nhiều lần
    /// -> cache theo (user, phiên). 5 phút đủ ngắn để tín hiệu mới kịp phản ánh.
    /// </summary>
    private static readonly TimeSpan SuggestionCacheTtl = TimeSpan.FromMinutes(5);

    private static readonly TimeSpan SettingsCacheTtl = TimeSpan.FromMinutes(5);

    private const string SettingsCacheKey = "tutor-suggestion:settings";

    public async Task<StudentTutorSuggestionResponse> GetForSessionAsync(
        string userId, Guid? sessionId, CancellationToken ct = default)
    {
        try
        {
            // Không có phiên (toggle ở menu tài khoản) -> chỉ cần biết học sinh bật/tắt.
            if (sessionId is null || sessionId == Guid.Empty)
            {
                var profileOnly = await studentRepo.FindByStudentOrLinkedUserAsync(userId);
                return profileOnly is null
                    ? Skip(TutorSuggestionSkipReasons.NotStudent)
                    : Skip(TutorSuggestionSkipReasons.NoSignal, profileOnly.Tutorsuggestionenabled);
            }

            var cacheKey = $"tutor-suggestion:{userId}:{sessionId}";
            if (cache.TryGetValue<StudentTutorSuggestionResponse>(cacheKey, out var cached) && cached is not null)
                return cached;

            var result = await BuildSuggestionAsync(userId, sessionId.Value, ct);
            var entryOptions = new MemoryCacheEntryOptions()
                .SetAbsoluteExpiration(SuggestionCacheTtl)
                // Tắt toggle -> huỷ token -> mọi phiên của user này rời cache ngay.
                .AddExpirationToken(new CancellationChangeToken(GetUserCacheToken(userId)));
            cache.Set(cacheKey, result, entryOptions);
            return result;
        }
        catch (Exception ex)
        {
            // Im lặng bỏ qua: banner không hiện còn hơn làm hỏng màn giải bài.
            logger.LogWarning(ex, "Không dựng được gợi ý gia sư (user {UserId}, session {SessionId})",
                userId, sessionId);
            return Skip(TutorSuggestionSkipReasons.NoSignal);
        }
    }

    private async Task<StudentTutorSuggestionResponse> BuildSuggestionAsync(
        string userId, Guid sessionId, CancellationToken ct)
    {
        // Hồ sơ học sinh: vừa để lấy lớp, vừa để biết em đã tắt gợi ý chưa.
        var profile = await studentRepo.FindByStudentOrLinkedUserAsync(userId);
        if (profile is null)
            return Skip(TutorSuggestionSkipReasons.NotStudent);

        var pref = profile.Tutorsuggestionenabled;

        var settings = await GetSettingsAsync(ct);
        if (!settings.Enabled)
            return Skip(TutorSuggestionSkipReasons.DisabledByAdmin, pref);
        if (!pref)
            return Skip(TutorSuggestionSkipReasons.DisabledByUser, pref);

        // Đã đặt lịch rồi thì không cần gợi ý nữa.
        var hasBooking = await context.Bookings
            .AsNoTracking()
            .AnyAsync(b => b.Studentid == profile.Studentid, ct);
        if (hasBooking)
            return Skip(TutorSuggestionSkipReasons.AlreadyBooked, pref);

        var weak = await GetWeakChaptersAsync(userId, sessionId, settings.MinConfidence, ct);
        if (weak.Count == 0)
            return Skip(TutorSuggestionSkipReasons.NoSignal, pref);
        if (weak[0].Count < settings.MinChapterCount)
            return Skip(TutorSuggestionSkipReasons.BelowThreshold, pref);

        var top = weak[0];

        // Học sinh dưới 16 / do phụ huynh quản lý vẫn thấy gợi ý, chỉ đổi nút thành
        // chia sẻ hồ sơ cho phụ huynh
        var ctaMode = TutorSuggestionCtaModes.Book;
        try
        {
            var eligibility = await studentService.GetBookingEligibilityAsync(userId);
            ctaMode = eligibility.CanBook
                ? TutorSuggestionCtaModes.Book
                : TutorSuggestionCtaModes.Share;
        }
        catch (Exception ex)
        {
            // Không chặn gợi ý vì không kiểm được eligibility; mặc định cho đặt lịch.
            logger.LogWarning(ex, "Không lấy được booking-eligibility cho user {UserId}", userId);
        }

        var levelOrder = await ResolveLevelOrderAsync(profile.Gradelevelid, ct);
        var recommendation = await tutorRecommendService.RecommendAsync(new TutorRecommendRequest
        {
            SubjectId = top.SubjectId,
            GradeLevelId = profile.Gradelevelid ?? top.GradeLevelId,
            Query = BuildQuery(weak, levelOrder),
            TopK = settings.TopK
        }, ct);

        if (recommendation.Tutors.Count == 0)
            return Skip(TutorSuggestionSkipReasons.NoTutorFound, pref);

        // Đánh giá đã bấm trong phiên này -> FE hiện lại trạng thái sau khi tải lại trang.
        var tutorIds = recommendation.Tutors.Select(t => t.TutorId).ToList();
        var myVotes = await context.TutorSuggestionVotes
            .AsNoTracking()
            .Where(v => v.UserId == userId && v.SessionId == sessionId && tutorIds.Contains(v.TutorId))
            .ToDictionaryAsync(v => v.TutorId, v => (int)v.Vote, ct);

        return new StudentTutorSuggestionResponse
        {
            ShouldShow = true,
            PreferenceEnabled = pref,
            MyTutorVotes = myVotes,
            CtaMode = ctaMode,
            BannerText = BuildBannerText(top),
            ModalTitle = $"Gia sư chuyên {top.Name}",
            ModalSubtitle = BuildModalSubtitle(weak, levelOrder),
            WeakChapters = weak,
            Tutors = recommendation.Tutors,
            SignalVersion = BuildSignalVersion(weak),
            SuggestionId = Guid.NewGuid()
        };
    }

    /// <summary>
    /// Đếm chương học sinh hỏi TRONG PHIÊN này (không gom lịch sử).
    /// </summary>
    private async Task<List<WeakChapterResponse>> GetWeakChaptersAsync(
        string userId, Guid sessionId, float minConfidence, CancellationToken ct)
    {
        var grouped = await context.StudentTopicSignals
            .AsNoTracking()
            .Where(s => s.SessionId == sessionId
                        && s.UserId == userId
                        && s.Confidence >= minConfidence)
            .GroupBy(s => new { s.ChapterSlug, s.Grade })
            .Select(g => new
            {
                g.Key.ChapterSlug,
                g.Key.Grade,
                Count = g.Count()
            })
            .ToListAsync(ct);

        if (grouped.Count == 0) return new List<WeakChapterResponse>();

        // Một slug có thể tồn tại ở NHIỀU lớp.
        var slugs = grouped.Select(g => g.ChapterSlug).Distinct().ToList();
        var chapters = await context.Chapters
            .AsNoTracking()
            .Where(c => slugs.Contains(c.Slug) && c.IsActive)
            .Join(context.Gradelevels.AsNoTracking(),
                c => c.GradeLevelId,
                gl => gl.Gradelevelid,
                (c, gl) => new { c.Slug, c.Name, c.SubjectId, c.GradeLevelId, gl.Levelorder })
            .ToListAsync(ct);

        return grouped
            .OrderByDescending(g => g.Count)
            .Take(TutorSuggestionDefaults.MaxWeakChapters)
            .Select(g =>
            {
                var candidates = chapters.Where(c => c.Slug == g.ChapterSlug).ToList();
                // Khớp theo lớp classifier đoán ("9" -> level_order 9); không khớp thì lấy bản đầu.
                var match = candidates.FirstOrDefault(c => string.Equals(
                                c.Levelorder.ToString(CultureInfo.InvariantCulture),
                                g.Grade,
                                StringComparison.Ordinal))
                            ?? candidates.FirstOrDefault();

                if (match is null)
                {
                    logger.LogWarning(
                        "Slug chương {Slug} do classifier sinh không có trong bảng chapters", g.ChapterSlug);
                }

                return new WeakChapterResponse
                {
                    Slug = g.ChapterSlug,
                    Name = match?.Name ?? Humanize(g.ChapterSlug),
                    Count = g.Count,
                    SubjectId = match?.SubjectId,
                    GradeLevelId = match?.GradeLevelId
                };
            })
            .ToList();
    }

    /// <summary>Gradelevelid (lớp 9 = 57) -> số lớp thật để ghép câu tiếng Việt.</summary>
    private async Task<int?> ResolveLevelOrderAsync(int? gradeLevelId, CancellationToken ct)
    {
        if (gradeLevelId is null) return null;
        return await context.Gradelevels
            .AsNoTracking()
            .Where(g => g.Gradelevelid == gradeLevelId)
            .Select(g => g.Levelorder)
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// Query cho vector rank ở tutora-ai
    /// </summary>
    private static string BuildQuery(IReadOnlyList<WeakChapterResponse> weak, int? levelOrder)
    {
        var names = string.Join(" và ", weak.Take(2).Select(w => w.Name));
        var lop = levelOrder is > 0 ? $"lớp {levelOrder} " : "";
        return $"Gia sư Toán {lop}kèm học sinh đang gặp khó ở chương {names}. "
             + "Ưu tiên giáo viên kiên nhẫn, giảng chắc lý thuyết nền và luyện bài tập cơ bản.";
    }

    private static string BuildBannerText(WeakChapterResponse top)
        => $"Bạn đã hỏi {top.Count} bài về {top.Name} — cần người kèm chương này?";

    private static string BuildModalSubtitle(IReadOnlyList<WeakChapterResponse> weak, int? levelOrder)
    {
        var lop = levelOrder is > 0 ? $" lớp {levelOrder}" : "";
        var names = string.Join(", ", weak.Select(w => w.Name));
        return $"Dựa trên các bài bạn vừa hỏi ({names}), đây là gia sư Toán{lop} phù hợp.";
    }

    /// <summary>
    /// Ký tín hiệu
    /// </summary>
    private static string BuildSignalVersion(IReadOnlyList<WeakChapterResponse> weak)
    {
        var raw = string.Join("|", weak.Select(w => $"{w.Slug}:{w.Count}"));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash, 0, 8).ToLowerInvariant();
    }

    /// <summary>Slug lạ (chưa có trong bảng chapters) -> vẫn hiện được tên đọc tạm được.</summary>
    private static string Humanize(string slug)
    {
        var text = slug.Replace('_', ' ').Trim();
        return text.Length == 0 ? slug : char.ToUpperInvariant(text[0]) + text[1..];
    }

    private static StudentTutorSuggestionResponse Skip(string reason, bool? preferenceEnabled = null)
        => new() { ShouldShow = false, SkipReason = reason, PreferenceEnabled = preferenceEnabled };

    // Tuỳ chọn của học sinh
    public async Task SetUserPreferenceAsync(string userId, bool enabled, CancellationToken ct = default)
    {
        var profile = await studentRepo.FindByStudentOrLinkedUserAsync(userId)
            ?? throw new KeyNotFoundException("Không tìm thấy hồ sơ học sinh.");

        profile.Tutorsuggestionenabled = enabled;
        await context.SaveChangesAsync(ct);

        // Đổi tuỳ chọn phải thấy hiệu lực ngay, không đợi cache 5 phút hết hạn.
        InvalidateUserCache(userId);
    }

    /// <summary>
    /// Cache gợi ý khoá theo
    /// </summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, CancellationTokenSource>
        UserCacheTokens = new();

    private static CancellationToken GetUserCacheToken(string userId)
        => UserCacheTokens.GetOrAdd(userId, _ => new CancellationTokenSource()).Token;

    private static void InvalidateUserCache(string userId)
    {
        if (UserCacheTokens.TryRemove(userId, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
        }
    }

    // Đánh giá gợi ý
    public async Task VoteTutorAsync(
        string userId, TutorSuggestionVoteRequest dto, CancellationToken ct = default)
    {
        if (!FeedbackVote.IsValid(dto.Vote))
            throw new ArgumentException("Giá trị đánh giá không hợp lệ.", nameof(dto));

        if (!TutorSuggestionFeedbackReasons.IsValid(dto.Reason))
            throw new ArgumentException("Lý do đánh giá không hợp lệ.", nameof(dto));

        // Lý do chỉ có nghĩa khi không hài lòng.
        var reason = dto.Vote == FeedbackVote.Dislike ? dto.Reason : null;
        var detail = dto.Vote == FeedbackVote.Dislike ? dto.Detail : null;
        var now = TimeZoneHelper.UtcNow;

        // Đánh giá là CHỐT (giống vote lời giải): bấm rồi thì không đổi được nữa.
        var daVote = await context.TutorSuggestionVotes
            .AnyAsync(v => v.SuggestionId == dto.SuggestionId
                           && v.TutorId == dto.TutorId
                           && v.UserId == userId, ct);
        if (daVote) return;

        context.TutorSuggestionVotes.Add(new TutorSuggestionVote
        {
            Id = Guid.NewGuid(),
            SuggestionId = dto.SuggestionId,
            SessionId = dto.SessionId,
            TutorId = dto.TutorId,
            UserId = userId,
            Vote = dto.Vote,
            Reason = reason,
            Detail = detail,
            ChapterSlug = dto.ChapterSlug,
            CreatedAt = now,
            UpdatedAt = now
        });

        await context.SaveChangesAsync(ct);

        // Kết quả gợi ý mang theo MyTutorVotes -> còn cache cũ thì tải lại trang sẽ thấy
        // như chưa bấm gì.
        InvalidateUserCache(userId);
    }

    // Cấu hình admin
    public async Task<TutorSuggestionSettingsResponse> GetSettingsAsync(CancellationToken ct = default)
    {
        if (cache.TryGetValue<TutorSuggestionSettingsResponse>(SettingsCacheKey, out var cached) && cached is not null)
            return cached;

        var keys = new[]
        {
            TutorSuggestionConfigKeys.Enabled,
            TutorSuggestionConfigKeys.MinChapterCount,
            TutorSuggestionConfigKeys.MinConfidence,
            TutorSuggestionConfigKeys.TopK
        };

        var rows = await context.Systemconfigs
            .AsNoTracking()
            .Where(c => keys.Contains(c.Configkey))
            .ToDictionaryAsync(c => c.Configkey, c => c.Configvalue, ct);

        var settings = new TutorSuggestionSettingsResponse
        {
            Enabled = ParseBool(rows, TutorSuggestionConfigKeys.Enabled, TutorSuggestionDefaults.Enabled),
            MinChapterCount = ParseInt(rows, TutorSuggestionConfigKeys.MinChapterCount, TutorSuggestionDefaults.MinChapterCount),
            MinConfidence = ParseFloat(rows, TutorSuggestionConfigKeys.MinConfidence, TutorSuggestionDefaults.MinConfidence),
            TopK = ParseInt(rows, TutorSuggestionConfigKeys.TopK, TutorSuggestionDefaults.TopK)
        };

        cache.Set(SettingsCacheKey, settings, SettingsCacheTtl);
        return settings;
    }

    public async Task<TutorSuggestionSettingsResponse> UpdateSettingsAsync(
        TutorSuggestionSettingsRequest dto, string? updatedByUserId, CancellationToken ct = default)
    {
        await UpsertConfigAsync(TutorSuggestionConfigKeys.Enabled,
            dto.Enabled ? "true" : "false",
            "Bật/tắt gợi ý gia sư trong app giải bài tập.", updatedByUserId, ct);

        await UpsertConfigAsync(TutorSuggestionConfigKeys.MinChapterCount,
            dto.MinChapterCount.ToString(CultureInfo.InvariantCulture),
            "Số bài cùng một chương trong một phiên thì mới gợi ý gia sư.", updatedByUserId, ct);

        await UpsertConfigAsync(TutorSuggestionConfigKeys.MinConfidence,
            dto.MinConfidence.ToString(CultureInfo.InvariantCulture),
            "Ngưỡng tin cậy của bộ phân loại để tín hiệu được tính (0-1).", updatedByUserId, ct);

        await UpsertConfigAsync(TutorSuggestionConfigKeys.TopK,
            dto.TopK.ToString(CultureInfo.InvariantCulture),
            "Số gia sư trả về cho thanh gợi ý.", updatedByUserId, ct);

        await context.SaveChangesAsync(ct);
        cache.Remove(SettingsCacheKey);

        return await GetSettingsAsync(ct);
    }

    private async Task UpsertConfigAsync(
        string key, string value, string description, string? updatedByUserId, CancellationToken ct)
    {
        var cfg = await context.Systemconfigs.FirstOrDefaultAsync(c => c.Configkey == key, ct);
        if (cfg is null)
        {
            cfg = new Systemconfig { Configkey = key, Description = description };
            context.Systemconfigs.Add(cfg);
        }
        cfg.Configvalue = value;
        cfg.Updatedby = updatedByUserId;
        cfg.Updatedat = TimeZoneHelper.UtcNow;
    }

    private static bool ParseBool(IReadOnlyDictionary<string, string?> rows, string key, bool fallback)
        => rows.TryGetValue(key, out var v) && bool.TryParse(v, out var parsed) ? parsed : fallback;

    private static int ParseInt(IReadOnlyDictionary<string, string?> rows, string key, int fallback)
        => rows.TryGetValue(key, out var v)
           && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;

    private static float ParseFloat(IReadOnlyDictionary<string, string?> rows, string key, float fallback)
        => rows.TryGetValue(key, out var v)
           && float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;
}
