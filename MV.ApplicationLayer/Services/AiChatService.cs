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

namespace MV.ApplicationLayer.Services;

public class AiChatService(
    IAiChatRepository aiChatRepo,
    IHttpClientFactory httpClientFactory,
    IConfiguration config,
    ILogger<AiChatService> logger) : IAiChatService
{
    private static readonly string[] AllowedRoles =
        { ChatHistoryRole.User, ChatHistoryRole.Assistant, ChatHistoryRole.System };

    // History tối đa nạp từ DB để gửi kèm /solve (tránh prompt quá dài)
    private const int HistoryWindow = 20;

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

    public async IAsyncEnumerable<string> SolveStreamAsync(
        string userId, Guid sessionId, AiSolveRequest dto,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var session = await GetOwnedSessionAsync(userId, sessionId);

        // 1. Lưu user message ngay (không mất kể cả nếu AI lỗi sau đó)
        var userContent = dto.Text ?? dto.ImageUrl ?? "[hình ảnh]";
        var now = TimeZoneHelper.UtcNow;
        var userMessage = new ChatHistory
        {
            MessageId = Guid.NewGuid(),
            SessionId = sessionId,
            Role = ChatHistoryRole.User,
            Content = userContent,
            ImageUrl = dto.ImageUrl,
            Grade = dto.Grade,
            CreatedAt = now
        };
        session.UpdatedAt = now;
        if (string.IsNullOrWhiteSpace(session.Title))
            session.Title = userContent.Length > 100 ? userContent[..100] : userContent;

        aiChatRepo.AddMessage(userMessage);
        aiChatRepo.UpdateSession(session);
        await aiChatRepo.SaveChangesAsync();

        // 2. Dựng history từ DB (cửa sổ gần nhất) thay vì để FE tự gửi
        var (recent, _) = await aiChatRepo.GetMessagesPagedAsync(sessionId, 1, HistoryWindow);
        var history = recent
            .Where(m => m.MessageId != userMessage.MessageId)
            .Select(m => new { role = m.Role, content = m.Content })
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
        var ragUsed = false;

        using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();

        using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        string? line;
        while ((line = await reader.ReadLineAsync(ct)) is not null)
        {
            // Bỏ qua dòng trống ngăn cách event (sẽ tự thêm lại "\n\n" ở controller)
            if (line.Length == 0) continue;
            if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;

            // Pass-through dòng "data: {...}" cho FE
            yield return line;

            // Gom delta để lưu assistant message khi xong
            var json = line["data:".Length..].Trim();
            if (json.Length == 0) continue;
            var (delta, rag) = TryExtractDelta(json);
            if (!string.IsNullOrEmpty(delta)) assistant.Append(delta);
            if (rag) ragUsed = true;
        }

        // 4. Lưu assistant message (do backend tự lưu, FE không cần gọi lại)
        if (assistant.Length > 0)
        {
            var finishedAt = TimeZoneHelper.UtcNow;
            aiChatRepo.AddMessage(new ChatHistory
            {
                MessageId = Guid.NewGuid(),
                SessionId = sessionId,
                Role = ChatHistoryRole.Assistant,
                Content = assistant.ToString(),
                Grade = dto.Grade,
                RagUsed = ragUsed,
                CreatedAt = finishedAt
            });
            session.UpdatedAt = finishedAt;
            aiChatRepo.UpdateSession(session);
            await aiChatRepo.SaveChangesAsync();
        }
    }

    private (string? Delta, bool RagUsed) TryExtractDelta(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            string? delta = root.TryGetProperty("delta", out var d) ? d.GetString() : null;
            bool rag = root.TryGetProperty("rag_used", out var r) && r.ValueKind == JsonValueKind.True;
            return (delta, rag);
        }
        catch (JsonException)
        {
            return (null, false);
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

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
