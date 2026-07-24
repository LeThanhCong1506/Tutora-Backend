using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MV.ApplicationLayer.RepositoryInterfaces;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.Entities;
using MV.DomainLayer.Exceptions;
using MV.DomainLayer.Helpers;
using MV.DomainLayer.Settings;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MV.ApplicationLayer.Services;

public class AiChatService(
    IAiChatRepository aiChatRepo,
    IQuestionNoteRepository questionNoteRepo,
    IHttpClientFactory httpClientFactory,
    IConfiguration config,
    IFileStorageService storage,
    ILogger<AiChatService> logger) : IAiChatService
{
    private static readonly string[] AllowedRoles =
        { ChatHistoryRole.User, ChatHistoryRole.Assistant, ChatHistoryRole.System };

    // History tối đa nạp từ DB để gửi kèm /solve (tránh prompt quá dài)
    private const int HistoryWindow = 20;

    private const string ImageBucket = "homework-images";

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
            Grade = dto.Grade,
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
        var payload = new
        {
            text = dto.Text,
            image_url = dto.ImageUrl,
            image_base64 = dto.ImageBase64,
            grade = dto.Grade,
            chapter = dto.Chapter,
            response_format = dto.ResponseFormat,
            chat_id = sessionId.ToString(),
            history
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
                var (delta, think, rag, stepsFinal) = TryExtractDelta(json);
                if (!string.IsNullOrEmpty(delta)) assistant.Append(delta);
                if (!string.IsNullOrEmpty(think)) thinking.Append(think);
                if (rag) ragUsed = true;
                if (!string.IsNullOrEmpty(stepsFinal)) stepsFinalJson = stepsFinal;
            }
        }
        finally
        {
            // 4. Lưu assistant message.
            if (assistant.Length > 0)
            {
                var finishedAt = TimeZoneHelper.UtcNow;
                string? metadata = null;
                if (thinking.Length > 0 || stepsFinalJson is not null)
                {
                    var meta = new JsonObject();
                    if (thinking.Length > 0) meta["thinking"] = thinking.ToString();
                    if (stepsFinalJson is not null) meta["steps"] = JsonNode.Parse(stepsFinalJson);
                    metadata = meta.ToJsonString();
                }
                aiChatRepo.AddMessage(new ChatHistory
                {
                    MessageId = Guid.NewGuid(),
                    SessionId = sessionId,
                    Role = ChatHistoryRole.Assistant,
                    Content = assistant.ToString(),
                    Metadata = metadata,
                    Grade = dto.Grade,
                    RagUsed = ragUsed,
                    CreatedAt = finishedAt
                });
                session.UpdatedAt = finishedAt;
                aiChatRepo.UpdateSession(session);
                await aiChatRepo.SaveChangesAsync();
            }
        }
    }

    private (string? Delta, string? Thinking, bool RagUsed, string? StepsFinal) TryExtractDelta(string json)
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
            return (delta, thinking, rag, stepsFinal);
        }
        catch (JsonException)
        {
            return (null, null, false, null);
        }
    }

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
