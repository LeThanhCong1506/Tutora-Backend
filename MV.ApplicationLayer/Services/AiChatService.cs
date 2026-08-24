using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MV.ApplicationLayer.Interfaces;
using MV.ApplicationLayer.RepositoryInterfaces;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.Entities;
using MV.DomainLayer.Exceptions;
using MV.DomainLayer.Helpers;
using MV.DomainLayer.Settings;
using System.Globalization;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MV.ApplicationLayer.Services;

public class AiChatService(
    IAiChatRepository aiChatRepo,
    IQuestionNoteRepository questionNoteRepo,
    IStudentRepository studentRepo,
    IUnitOfWork unitOfWork,
    IHttpClientFactory httpClientFactory,
    IConfiguration config,
    IFileStorageService storage,
    IAiCreditService aiCreditService,
    IMemoryCache cache,
    ILogger<AiChatService> logger) : IAiChatService
{
    /// <summary>Mỗi lần hỏi bài trừ đúng 1 credit.</summary>
    private const int SolveCreditCost = 1;

    private static readonly string[] AllowedRoles =
        { ChatHistoryRole.User, ChatHistoryRole.Assistant, ChatHistoryRole.System };

    // History tối đa nạp từ DB để gửi kèm /solve (tránh prompt quá dài)
    private const int HistoryWindow = 20;

    private const string ImageBucket = "homework-images";

    /// <summary>Trên ngưỡng này coi như bank đã có bài tương đương -> không thêm nữa.</summary>
    private const double NewQuestionSimilarityCeiling = 0.88;

    public async Task<AiChatSessionResponse> CreateSessionAsync(string userId, AiChatSessionCreateRequest dto)
    {
        var sessionType = dto.SessionType == ChatSessionType.TutorMatching
            ? ChatSessionType.TutorMatching
            : ChatSessionType.Homework;
        var now = TimeZoneHelper.UtcNow;

        var session = new ChatSession
        {
            SessionId = Guid.NewGuid(),
            UserId = userId,
            SessionType = sessionType,
            Title = dto.Title,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now
        };

        aiChatRepo.AddSession(session);
        await aiChatRepo.SaveChangesAsync();

        return ToSessionResponse(session);
    }

    public async Task<List<AiChatSessionResponse>> GetMySessionsAsync(string userId, string? sessionType = null)
    {
        var sessions = await aiChatRepo.GetSessionsByUserAsync(userId, sessionType);
        return sessions.Select(ToSessionResponse).ToList();
    }

    public async Task<PagedList<AiChatMessageResponse>> GetMessagesAsync(string userId, Guid sessionId, int page, int pageSize)
    {
        await GetOwnedSessionAsync(userId, sessionId);

        var (items, total) = await aiChatRepo.GetMessagesPagedAsync(sessionId, page, pageSize);
        var dtos = items.Select(ToMessageResponse).ToList();

        var myVotes = await aiChatRepo.GetMyVotesAsync(items.Select(m => m.MessageId), userId);
        if (myVotes.Count > 0)
        {
            foreach (var m in dtos)
                if (Guid.TryParse(m.MessageId, out var id) && myVotes.TryGetValue(id, out var v))
                    m.MyVote = v;
        }

        var savedTitles = (await questionNoteRepo.GetSavedTitlesBySessionAsync(userId, sessionId))
            .ToHashSet();
        if (savedTitles.Count > 0)
        {
            string? lastUserTitle = null;
            foreach (var m in dtos)
            {
                if (m.Role == ChatHistoryRole.User)
                    lastUserTitle = m.Content.Length > 255 ? m.Content[..255] : m.Content;
                else if (m.Role == ChatHistoryRole.Assistant && lastUserTitle is not null)
                    m.NoteSaved = savedTitles.Contains(lastUserTitle);
            }
        }

        return new PagedList<AiChatMessageResponse>(dtos, total, page, pageSize);
    }

    public async Task<AiChatMessageResponse> AddMessageAsync(string userId, Guid sessionId, AiChatMessageCreateRequest dto)
    {
        var session = await GetOwnedSessionAsync(userId, sessionId);

        var role = AllowedRoles.Contains(dto.Role) ? dto.Role : ChatHistoryRole.User;
        var now = TimeZoneHelper.UtcNow;

        var message = new ChatHistory
        {
            MessageId = Guid.NewGuid(),
            SessionId = sessionId,
            Role = role,
            Content = dto.Content,
            ImageUrl = dto.ImageUrl,
            Grade = dto.Grade,
            RagUsed = dto.RagUsed,
            Metadata = dto.Metadata != null ? JsonSerializer.Serialize(dto.Metadata) : null,
            CreatedAt = now
        };

        session.UpdatedAt = now;
        // Lấy tin nhắn đầu của user làm tiêu đề nếu phiên chưa có tiêu đề.
        if (string.IsNullOrWhiteSpace(session.Title) && role == ChatHistoryRole.User)
            session.Title = dto.Content.Length > 100 ? dto.Content[..100] : dto.Content;

        aiChatRepo.AddMessage(message);
        aiChatRepo.UpdateSession(session);
        await aiChatRepo.SaveChangesAsync();

        return ToMessageResponse(message);
    }

    public async Task DeleteSessionAsync(string userId, Guid sessionId)
    {
        var session = await GetOwnedSessionAsync(userId, sessionId);
        aiChatRepo.RemoveSession(session);
        await aiChatRepo.SaveChangesAsync();
    }

    public Task<int> DeleteAllSessionsAsync(string userId, string? sessionType = null)
        => aiChatRepo.RemoveSessionsByUserAsync(userId, sessionType);

    public async IAsyncEnumerable<string> SolveStreamAsync(
        string userId, Guid sessionId, AiSolveRequest dto,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var session = await GetOwnedSessionAsync(userId, sessionId);

        //    Chỉ trừ 1 lượt sau khi giải thành công (ở finally) — lỗi giữa chừng không mất lượt.
        var balance = (await aiCreditService.GetBalanceAsync(userId, ct)).Balance;
        if (balance < SolveCreditCost)
            throw new BookingException(
                AiCreditErrorCodes.InsufficientCredits,
                "Bạn đã hết lượt hỏi AI. Vui lòng nâng cấp để tiếp tục.", 402);

        // Client không gửi lớp -> lấy từ hồ sơ học sinh.
        var grade = dto.Grade ?? await ResolveStudentGradeAsync(userId, ct);
        var proficiency = await ResolveProficiencyAsync(userId, ct);

        // 1. Lưu user message ngay (không mất kể cả nếu AI lỗi sau đó).
        // Ảnh base64 -> upload lấy URL để mở lại phiên cũ vẫn thấy đề bài.
        var imageUrl = dto.ImageUrl ?? await TryUploadImageAsync(dto.ImageBase64, userId);
        var userContent = !string.IsNullOrWhiteSpace(dto.Text) ? dto.Text : "[hình ảnh]";
        var now = TimeZoneHelper.UtcNow;
        var userMessage = new ChatHistory
        {
            MessageId = Guid.NewGuid(),
            SessionId = sessionId,
            Role = ChatHistoryRole.User,
            Content = userContent,
            ImageUrl = imageUrl,
            Grade = grade,
            CreatedAt = now
        };
        session.UpdatedAt = now;
        if (string.IsNullOrWhiteSpace(session.Title))
            session.Title = userContent.Length > 100 ? userContent[..100] : userContent;

        aiChatRepo.AddMessage(userMessage);
        aiChatRepo.UpdateSession(session);
        await aiChatRepo.SaveChangesAsync();

        // 2. Dựng history từ DB (cửa sổ gần nhất) thay vì để FE tự gửi.
        var (recent, _) = await aiChatRepo.GetMessagesPagedAsync(sessionId, 1, HistoryWindow);
        var history = recent
            .Where(m => m.MessageId != userMessage.MessageId)
            .Select(m => new { role = m.Role, content = BuildHistoryContent(m) })
            .ToList();

        // 3. Gọi tutora-ai /solve và stream pass-through, đồng thời gom assistant content
        var client = httpClientFactory.CreateClient(ServiceKeys.HttpClients.TutorAi);
        // Sinh id của assistant message TỪ ĐÂY rồi truyền xuống tutora-ai, để `id` trong
        // SSE trùng với message_id lưu ở DB. Không làm vậy thì python tự sinh uuid khác và
        // FE không có cách nào biết id thật để gắn đánh giá like/dislike.
        var assistantMessageId = Guid.NewGuid();
        var payload = new
        {
            text = dto.Text,
            image_url = dto.ImageUrl,
            image_base64 = dto.ImageBase64,
            grade,
            chapter = dto.Chapter,
            response_format = dto.ResponseFormat,
            chat_id = sessionId.ToString(),
            message_id = assistantMessageId.ToString(),
            history,
            // Hồ sơ trình độ từ bài đánh giá đầu vào -> tutora-ai chọn độ sâu lời giải.
            proficiency
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/solve")
        {
            Content = JsonContent.Create(payload)
        };
        var apiKey = config[$"{TutorAiSettings.SectionName}:ApiKey"];
        if (!string.IsNullOrEmpty(apiKey))
            req.Headers.Add("X-API-Key", apiKey);

        var assistant = new StringBuilder();
        // Gom phần "suy nghĩ" (event `thinking`, tách khỏi delta)
        var thinking = new StringBuilder();
        var ragUsed = false;
        // Danh sách bước cấu trúc cuối cùng (raw JSON array) để lưu Metadata -> canvas.
        string? stepsFinalJson = null;
        AnswerTrust? trust = null;
        // Tag chủ đề (lớp/chương/dạng) tutora-ai gắn vào done event.
        TopicClassification? classification = null;

        try
        {
            using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();

            using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var reader = new StreamReader(stream);

            string? line;
            while ((line = await reader.ReadLineAsync(ct)) is not null)
            {
                if (line.Length == 0) continue;
                if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;

                yield return line;

                // Gom delta (lời giải) + thinking (suy nghĩ) để lưu message khi xong
                var json = line["data:".Length..].Trim();
                if (json.Length == 0) continue;
                var (delta, think, rag, stepsFinal, clf, tr) = TryExtractDelta(json);
                if (!string.IsNullOrEmpty(delta)) assistant.Append(delta);
                if (!string.IsNullOrEmpty(think)) thinking.Append(think);
                if (rag) ragUsed = true;
                if (!string.IsNullOrEmpty(stepsFinal)) stepsFinalJson = stepsFinal;
                if (clf is not null) classification = clf;
                if (tr is not null) trust = tr;
            }
        }
        finally
        {
            if (assistant.Length > 0)
            {
                var finishedAt = TimeZoneHelper.UtcNow;
                string? metadata = null;
                if (thinking.Length > 0 || stepsFinalJson is not null || trust is not null)
                {
                    var meta = new JsonObject();
                    if (thinking.Length > 0) meta["thinking"] = thinking.ToString();
                    if (stepsFinalJson is not null) meta["steps"] = JsonNode.Parse(stepsFinalJson);
                    if (trust is not null)
                        meta["trust"] = new JsonObject
                        {
                            ["bankVerified"] = trust.BankVerified,
                            ["ragUsed"] = ragUsed,
                            ["verified"] = trust.Verified,
                            ["similarity"] = trust.Similarity,
                            ["questionId"] = trust.QuestionId,
                        };
                    metadata = meta.ToJsonString();
                }
                aiChatRepo.AddMessage(new ChatHistory
                {
                    MessageId = assistantMessageId,
                    SessionId = sessionId,
                    Role = ChatHistoryRole.Assistant,
                    Content = assistant.ToString(),
                    Metadata = metadata,
                    // Lớp từ hồ sơ; classifier đoán được thì dùng làm dự phòng.
                    Grade = grade ?? classification?.Grade,
                    RagUsed = ragUsed,
                    // Đo được chất lượng RAG theo thời gian
                    RagSimilarity = (float?)trust?.Similarity,
                    RagQuestionId = Guid.TryParse(trust?.QuestionId, out var qid) ? qid : null,
                    AnswerVerified = trust?.Verified,
                    CreatedAt = finishedAt
                });
                session.UpdatedAt = finishedAt;
                aiChatRepo.UpdateSession(session);
                await aiChatRepo.SaveChangesAsync();

                await TrackTopicSignalAsync(classification, userId, sessionId, assistantMessageId, finishedAt);

                await QueueForReviewAsync(userMessage.Content, assistant.ToString(), trust, classification, userId);

                // Trừ 1 credit ngay tại đây.
                try
                {
                    await aiCreditService.SpendAsync(
                        userId, SolveCreditCost,
                        referenceId: null,
                        description: null,
                        ct: CancellationToken.None);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Không trừ được AI credit cho session {SessionId} user {UserId}", sessionId, userId);
                }
            }
        }
    }

    public async Task<AssistantRespondResponse> RespondAsync(
        string? userId, AssistantRespondRequest dto, CancellationToken ct = default)
    {
        var isAuthed = !string.IsNullOrEmpty(userId);
        ChatSession? session = null;

        // 1. History: authed dựng từ DB (nguồn sự thật); anonymous dùng history FE gửi.
        List<object> history;
        if (isAuthed)
        {
            session = await GetOrCreateMatchingSessionAsync(userId!, dto.SessionId, ct);
            var (recent, _) = await aiChatRepo.GetMessagesPagedAsync(session.SessionId, 1, HistoryWindow);
            history = recent
                .Select(m => (object)new { role = m.Role, content = m.Content })
                .ToList();

            // Lưu user message NGAY (không mất kể cả nếu AI lỗi sau đó).
            var nowUser = TimeZoneHelper.UtcNow;
            aiChatRepo.AddMessage(new ChatHistory
            {
                MessageId = Guid.NewGuid(),
                SessionId = session.SessionId,
                Role = ChatHistoryRole.User,
                Content = dto.Message,
                CreatedAt = nowUser
            });
            if (string.IsNullOrWhiteSpace(session.Title))
                session.Title = dto.Message.Length > 100 ? dto.Message[..100] : dto.Message;
            session.UpdatedAt = nowUser;
            aiChatRepo.UpdateSession(session);
            await aiChatRepo.SaveChangesAsync();
        }
        else
        {
            history = dto.History
                .Select(h => (object)new { role = h.Role, content = h.Content })
                .ToList();
        }

        // 2. Forward tutora-ai /api/v1/assistant/respond (payload snake_case).
        var client = httpClientFactory.CreateClient(ServiceKeys.HttpClients.TutorAi);
        var payload = new
        {
            history,
            message = dto.Message,
            context = dto.Context is null ? null : new
            {
                subject_id = dto.Context.SubjectId,
                grade_level_id = dto.Context.GradeLevelId,
                teaching_mode = dto.Context.TeachingMode,
                city = dto.Context.City,
                min_rate = dto.Context.MinRate,
                max_rate = dto.Context.MaxRate,
                tutor_gender = dto.Context.TutorGender
            },
            current_filters = dto.CurrentFilters is null ? null : new
            {
                min_rate = dto.CurrentFilters.MinRate,
                max_rate = dto.CurrentFilters.MaxRate,
                tutor_gender = dto.CurrentFilters.TutorGender,
                subject_id = dto.CurrentFilters.SubjectId,
                desired_count = dto.CurrentFilters.DesiredCount
            },
            shown_tutors = dto.ShownTutors.Select(t => new
            {
                tutor_id = t.TutorId,
                name = t.Name
            }).ToList()
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/assistant/respond")
        {
            Content = JsonContent.Create(payload)
        };
        var apiKey = config[$"{TutorAiSettings.SectionName}:ApiKey"];
        if (!string.IsNullOrEmpty(apiKey))
            req.Headers.Add("X-API-Key", apiKey);

        using var resp = await client.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        var raw = await resp.Content.ReadAsStringAsync(ct);
        var result = ParseAssistantResponse(raw);

        // 3. Authed: lưu assistant reply (kèm cards vào Metadata để mở lại phiên thấy card).
        if (isAuthed && session is not null)
        {
            var nowAsst = TimeZoneHelper.UtcNow;
            string? metadata = null;
            if (result.Cards.Count > 0 || result.Suggestions.Count > 0)
            {
                var meta = new JsonObject
                {
                    ["intent"] = result.Intent,
                    ["cards"] = JsonNode.Parse(JsonSerializer.Serialize(result.Cards)),
                    ["suggestions"] = JsonNode.Parse(JsonSerializer.Serialize(result.Suggestions))
                };
                metadata = meta.ToJsonString();
            }
            aiChatRepo.AddMessage(new ChatHistory
            {
                MessageId = Guid.NewGuid(),
                SessionId = session.SessionId,
                Role = ChatHistoryRole.Assistant,
                Content = result.Reply,
                Metadata = metadata,
                CreatedAt = nowAsst
            });
            session.UpdatedAt = nowAsst;
            aiChatRepo.UpdateSession(session);
            await aiChatRepo.SaveChangesAsync();

            result.SessionId = session.SessionId.ToString();
        }

        return result;
    }

    /// <summary>Phiên tutor_matching cho user: lấy theo SessionId (kiểm chủ quyền) hoặc tạo mới.</summary>
    private async Task<ChatSession> GetOrCreateMatchingSessionAsync(string userId, Guid? sessionId, CancellationToken ct)
    {
        if (sessionId is { } sid)
            return await GetOwnedSessionAsync(userId, sid);

        var now = TimeZoneHelper.UtcNow;
        var session = new ChatSession
        {
            SessionId = Guid.NewGuid(),
            UserId = userId,
            SessionType = ChatSessionType.TutorMatching,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now
        };
        aiChatRepo.AddSession(session);
        await aiChatRepo.SaveChangesAsync();
        return session;
    }

    /// <summary>Parse response snake_case của tutora-ai (WebChatResponse) → DTO camelCase.</summary>
    private AssistantRespondResponse ParseAssistantResponse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var res = new AssistantRespondResponse
            {
                Reply = root.TryGetProperty("reply", out var r) ? r.GetString() ?? "" : "",
                Intent = root.TryGetProperty("intent", out var i) ? i.GetString() ?? "tutor" : "tutor",
                AiRanked = root.TryGetProperty("ai_ranked", out var ar) && ar.ValueKind == JsonValueKind.True
            };

            if (root.TryGetProperty("cards", out var cards) && cards.ValueKind == JsonValueKind.Array)
                foreach (var c in cards.EnumerateArray())
                    res.Cards.Add(new AssistantCardDto
                    {
                        TutorId = GetStr(c, "tutor_id") ?? "",
                        Name = GetStr(c, "name") ?? "Gia sư",
                        AvatarUrl = GetStr(c, "avatar_url"),
                        IsBestMatch = c.TryGetProperty("is_best_match", out var bm) && bm.ValueKind == JsonValueKind.True,
                        PricePerHour = GetNum(c, "price_per_hour"),
                        Rating = GetNum(c, "rating"),
                        TotalReviews = GetInt(c, "total_reviews"),
                        Highlights = GetStrList(c, "highlights"),
                        ProfileUrl = GetStr(c, "profile_url") ?? "",
                        CtaLabel = GetStr(c, "cta_label") ?? "Xem chi tiết"
                    });

            if (root.TryGetProperty("filters", out var f) && f.ValueKind == JsonValueKind.Object)
                res.Filters = new AssistantFiltersOut
                {
                    MinRate = GetNum(f, "min_rate"),
                    MaxRate = GetNum(f, "max_rate"),
                    TutorGender = GetStr(f, "tutor_gender"),
                    SubjectId = GetInt(f, "subject_id"),
                    DesiredCount = GetInt(f, "desired_count")
                };

            res.Suggestions = GetStrList(root, "suggestions");

            if (root.TryGetProperty("shown_tutors", out var shown) && shown.ValueKind == JsonValueKind.Array)
                foreach (var t in shown.EnumerateArray())
                    res.ShownTutors.Add(new AssistantShownTutorOut
                    {
                        TutorId = GetStr(t, "tutor_id") ?? "",
                        Name = GetStr(t, "name")
                    });

            return res;
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Không parse được response trợ lý AI: {Json}", json);
            return new AssistantRespondResponse { Reply = "Xin lỗi, mình đang gặp trục trặc. Bạn thử lại sau nhé!" };
        }
    }

    private static string? GetStr(JsonElement e, string key)
        => e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static double? GetNum(JsonElement e, string key)
        => e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;

    private static int? GetInt(JsonElement e, string key)
        => e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : null;

    private static List<string> GetStrList(JsonElement e, string key)
    {
        var list = new List<string>();
        if (e.TryGetProperty(key, out var arr) && arr.ValueKind == JsonValueKind.Array)
            foreach (var x in arr.EnumerateArray())
                if (x.ValueKind == JsonValueKind.String && x.GetString() is { } s)
                    list.Add(s);
        return list;
    }

    public async Task VoteMessageAsync(
        string userId, Guid messageId, AiMessageVoteRequest dto, CancellationToken ct = default)
    {
        if (!FeedbackVote.IsValid(dto.Vote))
            throw new ArgumentException("Giá trị đánh giá không hợp lệ.", nameof(dto));

        if (!AiMessageFeedbackReasons.IsValid(dto.Reason))
            throw new ArgumentException("Lý do đánh giá không hợp lệ.", nameof(dto));

        if (!await aiChatRepo.IsMessageOwnedByUserAsync(messageId, userId))
            throw new KeyNotFoundException("Không tìm thấy tin nhắn.");

        // Lý do chỉ có nghĩa khi không hài lòng — like kèm lý do là dữ liệu rác.
        var reason = dto.Vote == FeedbackVote.Dislike ? dto.Reason : null;
        var detail = dto.Vote == FeedbackVote.Dislike ? dto.Detail : null;
        var now = TimeZoneHelper.UtcNow;

        var existing = await aiChatRepo.FindMessageVoteAsync(messageId, userId);
        if (existing is not null) return;

        aiChatRepo.AddMessageVote(new AiMessageVote
        {
            Id = Guid.NewGuid(),
            MessageId = messageId,
            UserId = userId,
            Vote = dto.Vote,
            Reason = reason,
            Detail = detail,
            CreatedAt = now,
            UpdatedAt = now
        });

        await aiChatRepo.SaveChangesAsync();
    }

    /// <summary>
    /// Lớp của học sinh dạng slug tutora-ai hiểu ("9", "10"...), suy từ Levelorder của hồ sơ.
    /// Cache 10 phút vì gọi mỗi lượt hỏi bài mà lớp thì gần như không đổi.
    /// Không phải học sinh (khách, phụ huynh, gia sư) -> null, giữ nguyên hành vi cũ.
    /// </summary>
    private async Task<string?> ResolveStudentGradeAsync(string userId, CancellationToken ct)
    {
        var cacheKey = $"ai-chat:student-grade:{userId}";
        if (cache.TryGetValue<string?>(cacheKey, out var cached)) return cached;

        string? grade = null;
        try
        {
            var profile = await studentRepo.FindByStudentOrLinkedUserAsync(userId);
            var levelOrder = profile?.GradelevelNavigation?.Levelorder;
            if (levelOrder is > 0)
                grade = levelOrder.Value.ToString(CultureInfo.InvariantCulture);
        }
        catch (Exception ex)
        {
            // Không có lớp thì classifier vẫn tự đoán được — không chặn việc giải bài.
            logger.LogWarning(ex, "Không lấy được lớp của user {UserId}", userId);
        }

        cache.Set(cacheKey, grade, TimeSpan.FromMinutes(10));
        return grade;
    }

    /// <summary>
    /// Hồ sơ trình độ gửi kèm /solve. Học sinh chưa làm bài đánh giá -> null, giải như cũ.
    /// Lấy profile mới cập nhật nhất (chưa có tín hiệu môn nào đang hỏi ở luồng solve).
    /// </summary>
    private async Task<object?> ResolveProficiencyAsync(string userId, CancellationToken ct)
    {
        var cacheKey = $"ai-chat:proficiency:{userId}";
        if (cache.TryGetValue<object?>(cacheKey, out var cached)) return cached;

        object? result = null;
        try
        {
            var profiles = await unitOfWork.AssessmentRepository.GetProficiencyProfilesAsync(userId);
            var profile = profiles
                .OrderByDescending(p => p.UpdatedAt ?? DateTime.MinValue)
                .FirstOrDefault();

            if (profile != null && (profile.Level != null || profile.Summary != null))
                result = new
                {
                    subject_name = profile.Subject?.Subjectname,
                    grade_name = profile.Gradelevel?.Gradename,
                    level = profile.Level,
                    summary = profile.Summary,
                    weak_chapters = ExtractChapters(profile.Weaknesses),
                    strong_chapters = ExtractChapters(profile.Strengths),
                };
        }
        catch (Exception ex)
        {
            // Thiếu profile không được chặn việc giải bài.
            logger.LogWarning(ex, "Không lấy được profile trình độ của user {UserId}", userId);
        }

        cache.Set(cacheKey, result, TimeSpan.FromMinutes(10));
        return result;
    }

    /// <summary>Rút tên chương từ cột JSON [{"chapter": "..."}]. Lỗi parse -> rỗng.</summary>
    private static List<string> ExtractChapters(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return new();

            return doc.RootElement.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.Object)
                .Select(e => e.TryGetProperty("chapter", out var c) ? c.GetString() : null)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name!)
                .ToList();
        }
        catch
        {
            return new();
        }
    }

    /// <summary>
    /// Bài học sinh hỏi mà bank KHÔNG có -> xếp hàng chờ gia sư duyệt.
    ///
    /// Bank tự lớn theo nhu cầu thật thay vì để staff soạn mò: câu nào có người hỏi mới
    /// vào kho. Duyệt xong (published) thì học sinh sau hỏi lại được RAG + gắn nhãn.
    /// Chỉ nhận bài AI tự tin (code Python xác nhận đáp số) — rác vào kho còn hại hơn kho rỗng.
    /// </summary>
    private async Task QueueForReviewAsync(
        string? question, string? solution, AnswerTrust? trust,
        TopicClassification? classification, string userId)
    {
        // Đã có bài gần giống trong bank -> không nhân bản.
        if (trust is null || trust.Similarity >= NewQuestionSimilarityCeiling) return;
        // Đáp số chưa được xác nhận -> chưa đủ tin để đưa gia sư xem.
        if (trust.Verified != true) return;
        if (string.IsNullOrWhiteSpace(question) || string.IsNullOrWhiteSpace(solution)) return;
        if (question.Length < 20 || question == "[hình ảnh]") return;

        try
        {
            await aiChatRepo.AddPendingQuestionAsync(
                content: question.Trim(),
                solution: solution.Trim(),
                chapter: classification?.Chapter,
                grade: classification?.Grade,
                createdBy: userId);
        }
        catch (Exception ex)
        {
            // Bánh đà hỏng KHÔNG được ảnh hưởng việc trả lời học sinh.
            logger.LogWarning(ex, "Không xếp được câu hỏi vào hàng chờ duyệt (user {UserId})", userId);
        }
    }

    /// <summary>
    /// Lưu tag chủ đề của lượt giải này để sau tổng hợp ra "chương đang vướng".
    /// </summary>
    private async Task TrackTopicSignalAsync(
        TopicClassification? classification,
        string userId,
        Guid sessionId,
        Guid messageId,
        DateTime createdAt)
    {
        if (classification is null) return;

        try
        {
            aiChatRepo.AddTopicSignal(new StudentTopicSignal
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                SessionId = sessionId,
                MessageId = messageId,
                Grade = classification.Grade,
                ChapterSlug = classification.Chapter,
                Topic = classification.Topic,
                Confidence = classification.Confidence,
                CreatedAt = createdAt
            });
            await aiChatRepo.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Không ghi được tín hiệu chủ đề (session {SessionId}, chapter {Chapter})",
                sessionId, classification.Chapter);
        }
    }

    private (string? Delta, string? Thinking, bool RagUsed, string? StepsFinal,
             TopicClassification? Classification, AnswerTrust? Trust)
        TryExtractDelta(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            string? delta = root.TryGetProperty("delta", out var d) ? d.GetString() : null;
            string? thinking = root.TryGetProperty("thinking", out var t) ? t.GetString() : null;
            bool rag = root.TryGetProperty("rag_used", out var r) && r.ValueKind == JsonValueKind.True;
            string? stepsFinal = root.TryGetProperty("steps_final", out var sf) && sf.ValueKind == JsonValueKind.Array
                ? sf.GetRawText()
                : null;
            var classification = ParseClassification(root);
            return (delta, thinking, rag, stepsFinal, classification, ParseTrust(root));
        }
        catch (JsonException)
        {
            return (null, null, false, null, null, null);
        }
    }

    /// <summary>Nhãn tin cậy ở done event. Lưu vào metadata để mở lại phiên cũ vẫn thấy nhãn.</summary>
    private sealed record AnswerTrust(bool BankVerified, bool? Verified, double? Similarity, string? QuestionId);

    private static AnswerTrust? ParseTrust(JsonElement root)
    {
        // Chỉ done event mới mang nhãn; delta thường không có field nào trong số này.
        if (!root.TryGetProperty("done", out var done) || done.ValueKind != JsonValueKind.True) return null;

        bool bankVerified = root.TryGetProperty("bank_verified", out var bv) && bv.ValueKind == JsonValueKind.True;
        bool? verified = root.TryGetProperty("verified", out var v)
            ? v.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => null,   // null = bài hình/chứng minh, không kết luận được
            }
            : null;
        double? similarity = root.TryGetProperty("bank_similarity", out var sim)
            && sim.ValueKind == JsonValueKind.Number ? sim.GetDouble() : null;
        string? questionId = root.TryGetProperty("bank_question_id", out var qid)
            && qid.ValueKind == JsonValueKind.String ? qid.GetString() : null;

        return new AnswerTrust(bankVerified, verified, similarity, questionId);
    }

    /// <summary>
    /// Tag chủ đề tutora-ai gắn vào done event. Chỉ nhận khi có chapter — không có
    /// chapter thì tín hiệu vô dụng cho việc gợi ý.
    /// </summary>
    private static TopicClassification? ParseClassification(JsonElement root)
    {
        if (!root.TryGetProperty("classification", out var c) || c.ValueKind != JsonValueKind.Object)
            return null;

        var chapter = c.TryGetProperty("chapter", out var ch) && ch.ValueKind == JsonValueKind.String
            ? ch.GetString()
            : null;
        if (string.IsNullOrWhiteSpace(chapter)) return null;

        return new TopicClassification(
            Grade: c.TryGetProperty("grade", out var g) && g.ValueKind == JsonValueKind.String
                ? g.GetString()
                : null,
            Chapter: chapter,
            Topic: c.TryGetProperty("topic", out var tp) && tp.ValueKind == JsonValueKind.String
                ? tp.GetString()
                : null,
            Confidence: c.TryGetProperty("confidence", out var cf)
                && cf.ValueKind == JsonValueKind.Number
                && cf.TryGetSingle(out var cfv)
                    ? cfv
                    : 0f);
    }

    private sealed record TopicClassification(string? Grade, string Chapter, string? Topic, float Confidence);

    private const string CanvasOpen = "【CANVAS】";
    private const string CanvasClose = "【HẾT CANVAS】";

    private static string BuildHistoryContent(ChatHistory m)
    {
        if (m.Role != ChatHistoryRole.Assistant || string.IsNullOrEmpty(m.Metadata))
            return m.Content;

        var canvas = RebuildCanvasMarkdown(m.Metadata);
        if (string.IsNullOrEmpty(canvas)) return m.Content;

        // Phần chat (Content) đứng trước, canvas hiện tại bọc marker theo sau.
        return $"{m.Content}\n\n{CanvasOpen}\n{canvas}\n{CanvasClose}";
    }

    private static string RebuildCanvasMarkdown(string metadataJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(metadataJson);
            if (!doc.RootElement.TryGetProperty("steps", out var steps)
                || steps.ValueKind != JsonValueKind.Array)
                return string.Empty;

            var sb = new StringBuilder();
            var stepNo = 0;
            foreach (var step in steps.EnumerateArray())
            {
                var title = step.TryGetProperty("title", out var t) ? t.GetString() : null;
                var explanation = step.TryGetProperty("explanation", out var e) ? e.GetString() : null;

                // Tiêu đề: "Phân tích đề"/"Kết luận" giữ nguyên; các bước còn lại đánh số.
                if (!string.IsNullOrWhiteSpace(title))
                {
                    if (title is "Phân tích đề" or "Kết luận" or "Lời giải")
                        sb.Append("**").Append(title).Append("**\n");
                    else
                        sb.Append("**Bước ").Append(++stepNo).Append(": ").Append(title).Append("**\n");
                }
                if (!string.IsNullOrWhiteSpace(explanation))
                    sb.Append(explanation).Append('\n');
                if (step.TryGetProperty("formulas", out var fs) && fs.ValueKind == JsonValueKind.Array)
                    foreach (var f in fs.EnumerateArray())
                        if (f.GetString() is { Length: > 0 } formula)
                            sb.Append("$$").Append(formula).Append("$$\n");
                sb.Append('\n');
            }
            return sb.ToString().Trim();
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }

    // Helpers

    /// <summary>
    /// Upload ảnh đề bài (base64) lên storage, trả public URL.
    /// </summary>
    private async Task<string?> TryUploadImageAsync(string? base64, string userId)
    {
        if (string.IsNullOrWhiteSpace(base64)) return null;

        try
        {
            // FE có thể gửi kèm prefix "data:image/png;base64," -> cắt bỏ.
            var payload = base64.Contains(',') ? base64[(base64.IndexOf(',') + 1)..] : base64;
            var bytes = Convert.FromBase64String(payload);

            await storage.EnsureBucketExistsAsync(ImageBucket);
            return await storage.UploadImageBytesAsync(ImageBucket, userId, bytes, $"{Guid.NewGuid()}.png");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Upload ảnh đề bài thất bại.");
            return null;
        }
    }

    private async Task<ChatSession> GetOwnedSessionAsync(string userId, Guid sessionId)
    {
        var session = await aiChatRepo.FindSessionByIdAsync(sessionId)
            ?? throw new AiChatSessionNotFoundException(sessionId);

        if (session.UserId != userId)
            throw new AiChatSessionForbiddenException();

        return session;
    }

    private static AiChatSessionResponse ToSessionResponse(ChatSession s) => new()
    {
        SessionId = s.SessionId.ToString(),
        SessionType = s.SessionType,
        Title = s.Title,
        IsActive = s.IsActive ?? true,
        CreatedAt = s.CreatedAt,
        UpdatedAt = s.UpdatedAt
    };

    private object? ParseMetadata(string? metadata)
    {
        if (string.IsNullOrEmpty(metadata)) return null;
        try
        {
            return JsonSerializer.Deserialize<object>(metadata);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Failed to parse AI chat message metadata; returning null.");
            return null;
        }
    }

    private AiChatMessageResponse ToMessageResponse(ChatHistory m) => new()
    {
        MessageId = m.MessageId.ToString(),
        SessionId = m.SessionId.ToString(),
        Role = m.Role,
        Content = m.Content,
        ImageUrl = m.ImageUrl,
        Metadata = ParseMetadata(m.Metadata),
        CreatedAt = m.CreatedAt
    };
}
