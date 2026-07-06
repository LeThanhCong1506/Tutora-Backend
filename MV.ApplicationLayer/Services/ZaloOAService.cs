using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.DTO.ResponseModel.Zalo;
using MV.ApplicationLayer.Interfaces;
using StackExchange.Redis;
using System.Net.Http.Json;
using System.Text.Json;
using MV.DomainLayer.Constants;

namespace MV.ApplicationLayer.Services;

public class ZaloOAService : IZaloOAService
{
    private static readonly SemaphoreSlim TokenRefreshLock = new(1, 1);

    private readonly ILogger<ZaloOAService> _logger;
    private readonly ZaloOAConfig _config;
    private readonly bool _isMockMode;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConnectionMultiplexer _redis;
    private readonly IServiceProvider _serviceProvider;

    public ZaloOAService(
        ILogger<ZaloOAService> logger,
        IOptions<ZaloOAConfig> config,
        IHttpClientFactory httpClientFactory,
        IConnectionMultiplexer redis,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _config = config.Value;
        _isMockMode = _config.MockMode || string.IsNullOrEmpty(_config.OAId);
        _httpClientFactory = httpClientFactory;
        _redis = redis;
        _serviceProvider = serviceProvider;
    }

    // ─── Token Management ───────────────────────────────────────────────────

    public async Task<string> GetOAAccessTokenAsync()
    {
        var db = _redis.GetDatabase();
        var cached = await db.StringGetAsync("zalo:oa:access_token");
        if (cached.HasValue) return cached!;

        await TokenRefreshLock.WaitAsync();
        try
        {
            // Another request may have refreshed the token while this request waited.
            cached = await db.StringGetAsync("zalo:oa:access_token");
            if (cached.HasValue) return cached!;

            return await RefreshWithFallbackAsync(db);
        }
        finally
        {
            TokenRefreshLock.Release();
        }
    }

    /// <summary>
    /// Refresh dùng refresh token trong Redis, fallback sang token cấu hình nếu Redis
    /// token hỏng.
    /// </summary>
    private async Task<string> RefreshWithFallbackAsync(IDatabase db)
    {
        var redisRefreshToken = (string?)await db.StringGetAsync("zalo:oa:refresh_token");
        var configuredRefreshToken = _config.RefreshToken;

        if (string.IsNullOrWhiteSpace(redisRefreshToken) &&
            string.IsNullOrWhiteSpace(configuredRefreshToken))
        {
            throw new InvalidOperationException("Zalo OA refresh token chưa được cấu hình.");
        }

        try
        {
            return await RefreshOAAccessTokenAsync(
                db,
                redisRefreshToken ?? configuredRefreshToken!);
        }
        catch (InvalidOperationException ex) when (
            !string.IsNullOrWhiteSpace(redisRefreshToken) &&
            !string.IsNullOrWhiteSpace(configuredRefreshToken) &&
            !string.Equals(redisRefreshToken, configuredRefreshToken, StringComparison.Ordinal))
        {
            _logger.LogWarning(ex, "Zalo refresh token trong Redis không hợp lệ; thử token từ cấu hình");
            await db.KeyDeleteAsync("zalo:oa:refresh_token");
            return await RefreshOAAccessTokenAsync(db, configuredRefreshToken);
        }
    }

    // Ngưỡng refresh chủ động: refresh khi access token còn dưới bấy nhiêu thời gian
    // sống. 
    private static readonly TimeSpan ProactiveRefreshThreshold = TimeSpan.FromHours(3);

    public async Task EnsureFreshTokenAsync()
    {
        if (_isMockMode) return;

        var db = _redis.GetDatabase();
        var ttl = await db.KeyTimeToLiveAsync("zalo:oa:access_token");

        // Token còn sống thoải mái -> KHÔNG refresh (tránh rotate refresh token vô ích;
        // mỗi lần refresh Zalo cấp refresh token mới và vô hiệu cái cũ).
        if (ttl.HasValue && ttl.Value > ProactiveRefreshThreshold)
            return;

        await TokenRefreshLock.WaitAsync();
        try
        {
            // Double-check dưới lock: instance/request khác có thể vừa refresh xong.
            ttl = await db.KeyTimeToLiveAsync("zalo:oa:access_token");
            if (ttl.HasValue && ttl.Value > ProactiveRefreshThreshold)
                return;

            // Refresh trực tiếp — KHÔNG xoá cache trước. RefreshOAAccessTokenAsync chỉ
            // ghi đè access token khi Zalo trả về thành công, nên token cũ (còn hạn) vẫn
            // an toàn nếu refresh fail vì Zalo lỗi tạm thời.
            await RefreshWithFallbackAsync(db);
            _logger.LogInformation("Zalo OA token proactively refreshed (ttl was {Ttl}).", ttl);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Proactive Zalo OA token refresh failed.");
        }
        finally
        {
            TokenRefreshLock.Release();
        }
    }

    private async Task<string> RefreshOAAccessTokenAsync(IDatabase db, string refreshToken)
    {
        var secretKey = _config.AppSecretKey ?? _config.SecretKey;
        if (string.IsNullOrWhiteSpace(_config.AppId) || string.IsNullOrWhiteSpace(secretKey))
        {
            throw new InvalidOperationException("Thiếu Zalo OA AppId hoặc AppSecretKey.");
        }

        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Add("secret_key", secretKey);
        var body = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>(OAuthFieldNames.RefreshToken, refreshToken),
            new KeyValuePair<string, string>(OAuthFieldNames.AppId, _config.AppId),
            new KeyValuePair<string, string>(OAuthFieldNames.GrantType, OAuthGrantTypes.RefreshToken),
        });

        var res = await client.PostAsync("https://oauth.zaloapp.com/v4/oa/access_token", body);
        res.EnsureSuccessStatusCode();
        var json = await res.Content.ReadFromJsonAsync<JsonElement>();

        if (json.TryGetProperty("error", out var errEl) && errEl.GetInt32() != 0)
        {
            var errMsg = json.TryGetProperty("error_name", out var nameEl)
                ? nameEl.GetString()
                : DisplayValues.Unknown;
            throw new InvalidOperationException($"Zalo token error: {errMsg} (code {errEl.GetInt32()})");
        }

        var newToken = json.GetProperty(OAuthFieldNames.AccessToken).GetString()!;
        var expiresIn = json.TryGetProperty("expires_in", out var expEl)
            ? (expEl.ValueKind == JsonValueKind.String ? long.Parse(expEl.GetString()!) : expEl.GetInt64())
            : 86400L;

        // Save access token
        await db.StringSetAsync("zalo:oa:access_token", newToken,
            TimeSpan.FromSeconds(Math.Max(expiresIn - 3600, 60)));

        if (json.TryGetProperty(OAuthFieldNames.RefreshToken, out var rtEl))
        {
            var newRefreshToken = rtEl.GetString();
            if (!string.IsNullOrEmpty(newRefreshToken))
            {
                await db.StringSetAsync("zalo:oa:refresh_token", newRefreshToken);
            }
        }

        _logger.LogInformation("Zalo OA access token refreshed successfully");
        return newToken;
    }

    // ─── OA Reply API ────────────────────────────────────────────────────────

    public async Task SendOAMessageAsync(string recipientZaloId, string text)
    {
        if (_isMockMode)
        {
            _logger.LogInformation("[MOCK] OA → {ZaloId}: {Text}", recipientZaloId, text);
            return;
        }
        var token = await GetOAAccessTokenAsync();
        var client = _httpClientFactory.CreateClient(ServiceKeys.HttpClients.ZaloOA);
        client.DefaultRequestHeaders.Add(OAuthFieldNames.AccessToken, token);
        var payload = new
        {
            recipient = new { user_id = recipientZaloId },
            message = new { text }
        };
        var res = await client.PostAsJsonAsync("v3.0/oa/message/cs", payload);
        var body = await res.Content.ReadAsStringAsync();
        _logger.LogInformation("OA message to {ZaloId}: status={Status}, response={Body}", recipientZaloId, res.StatusCode, body);
    }

    public async Task SendOAMessageWithButtonsAsync(string recipientZaloId, string text, List<ZaloQuickReply> buttons)
    {
        if (_isMockMode)
        {
            _logger.LogInformation("[MOCK] OA → {ZaloId}: {Text} [Buttons: {Count}]",
                recipientZaloId, text, buttons.Count);
            return;
        }
        var token = await GetOAAccessTokenAsync();
        var client = _httpClientFactory.CreateClient(ServiceKeys.HttpClients.ZaloOA);
        client.DefaultRequestHeaders.Add(OAuthFieldNames.AccessToken, token);
        var payload = new
        {
            recipient = new { user_id = recipientZaloId },
            message = new
            {
                text,
                attachment = new
                {
                    type = "template",
                    payload = new
                    {
                        template_type = "quick_reply",
                        elements = buttons.Select(b => new
                        {
                            title = b.Title,
                            type = b.Type,
                            payload = b.Payload,
                            image_icon = b.ImageIcon
                        })
                    }
                }
            }
        };
        var res = await client.PostAsJsonAsync("v3.0/oa/message/cs", payload);
        _logger.LogInformation("OA buttons message sent to {ZaloId}: {Status}", recipientZaloId, res.StatusCode);
    }

    // ─── Webhook Handler ─────────────────────────────────────────────────────

    public async Task HandleWebhookAsync(string payload)
    {
        if (_isMockMode)
        {
            _logger.LogInformation("[MOCK] Webhook: {Payload}", payload);
            return;
        }

        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;
        var eventName = root.TryGetProperty("event_name", out var ev) ? ev.GetString() : null;
        var senderId = root.TryGetProperty("sender", out var sender)
            && sender.TryGetProperty("id", out var sid) ? sid.GetString() : null;

        if (string.IsNullOrEmpty(senderId))
        {
            _logger.LogWarning("Webhook missing sender.id");
            return;
        }

        switch (eventName)
        {
            case ZaloOAEventType.Follow:
                await HandleFollowAsync(senderId);
                break;
            case ZaloOAEventType.UserSendText:
                var messageText = root.TryGetProperty("message", out var msg)
                    && msg.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
                using (var scope = _serviceProvider.CreateScope())
                {
                    var chatbot = scope.ServiceProvider.GetRequiredService<IZaloChatbotService>();
                    await chatbot.HandleUserMessageAsync(senderId, messageText);
                }
                break;
            case ZaloOAEventType.Unfollow:
                _logger.LogInformation("User {ZaloId} unfollowed OA", senderId);
                break;
            default:
                _logger.LogDebug("Unhandled event: {EventName}", eventName);
                break;
        }
    }

    private async Task HandleFollowAsync(string senderZaloId)
    {
        _logger.LogInformation("User {ZaloId} followed OA", senderZaloId);
        await SendOAMessageWithButtonsAsync(senderZaloId,
            "Chào mừng bạn đến với Tutora! Tôi có thể giúp gì?",
            new List<ZaloQuickReply>
            {
                new() { Title = "Tìm gia sư",     Payload = ZaloChatbotIntent.FindTutor },
                new() { Title = "Xem lịch học",   Payload = ZaloChatbotIntent.ViewCalendar },
                new() { Title = "Liên hệ hỗ trợ", Payload = ZaloChatbotIntent.Contact }
            });
    }

    // ─── ZNS / Notifications ─────────────────────────────────────────────────

    public async Task<ZaloSendResult> SendZnsOtpAsync(string phone, string otp)
    {
        if (_isMockMode)
        {
            _logger.LogInformation("[MOCK] ZNS OTP {Otp} → phone={Phone}", otp, phone);
            return new ZaloSendResult
            {
                Success = true,
                MessageId = $"mock_{Guid.NewGuid():N}"
            };
        }

        if (string.IsNullOrWhiteSpace(_config.ZnsTemplateOtp))
        {
            return new ZaloSendResult
            {
                Success = false,
                Error = "OTP template chưa cấu hình"
            };
        }

        var templateData = new Dictionary<string, string> { ["otp"] = otp };
        return await SendZnsTemplateAsync(phone, _config.ZnsTemplateOtp, templateData);
    }

    public async Task<ZaloSendResult> SendClassSessionReportAsync(int classSessionId)
    {
        if (_isMockMode)
        {
            _logger.LogInformation("[MOCK] ZNS classSession report for classSession {ClassSessionId}", classSessionId);
            return new ZaloSendResult { Success = true, MessageId = $"mock_{classSessionId}" };
        }
        // NOTE: ZNS classSession report sending is pending ZNS template configuration.
        // When ready: query ClassSession + User + LessonReport, then POST to
        // https://business.openapi.zalo.me/message/template with the configured template ID.
        _logger.LogInformation("ZNS classSession report triggered for classSession {ClassSessionId} — template not configured", classSessionId);
        return new ZaloSendResult { Success = true };
    }

    public async Task<ZaloSendResult> SendNotificationAsync(string userId, string templateId, Dictionary<string, string> data)
    {
        if (_isMockMode)
        {
            _logger.LogInformation("[MOCK] ZNS → {UserId} template {TemplateId}", userId, templateId);
            return new ZaloSendResult { Success = true, MessageId = $"mock_{Guid.NewGuid():N}" };
        }

        // Resolve ZNS template ID từ config
        var znsTemplateId = templateId switch
        {
            ZnsTemplateType.LessonReminder => _config.ZnsTemplateLessonReminder,
            ZnsTemplateType.BookingConfirmed => _config.ZnsTemplateBookingConfirmed,
            ZnsTemplateType.LessonReport => _config.ZnsTemplateLessonReport,
            ZnsTemplateType.PayoutProcessed => _config.ZnsTemplatePayoutProcessed,
            _ => null
        };

        if (string.IsNullOrEmpty(znsTemplateId))
        {
            _logger.LogWarning("ZNS template '{TemplateId}' chưa được cấu hình — bỏ qua", templateId);
            return new ZaloSendResult { Success = false, Error = "Template not configured" };
        }

        // Lấy Zalo phone number của user từ DB
        using var scope = _serviceProvider.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var user = await unitOfWork.UserRepository.GetUserByIdAsync(userId);
        if (user == null || string.IsNullOrEmpty(user.Phone))
        {
            _logger.LogWarning("ZNS: user {UserId} không có số điện thoại", userId);
            return new ZaloSendResult { Success = false, Error = "User phone not found" };
        }

        return await SendZnsTemplateAsync(user.Phone, znsTemplateId, data);
    }

    private async Task<ZaloSendResult> SendZnsTemplateAsync(
        string phone,
        string templateId,
        Dictionary<string, string> templateData)
    {
        try
        {
            var znsPhone = NormalizeVietnamPhoneForZns(phone);
            var token = await GetOAAccessTokenAsync();
            var client = _httpClientFactory.CreateClient(ServiceKeys.HttpClients.ZaloZNS);
            client.DefaultRequestHeaders.Add(OAuthFieldNames.AccessToken, token);

            var payload = new Dictionary<string, object>
            {
                ["phone"] = znsPhone,
                ["template_id"] = templateId,
                ["template_data"] = templateData,
                ["tracking_id"] = Guid.NewGuid().ToString("N")
            };

            var res = await client.PostAsJsonAsync("message/template", payload);
            var responseBody = await res.Content.ReadAsStringAsync();
            _logger.LogInformation(
                "ZNS response: phone={Phone}, status={Status}, body={Body}",
                znsPhone,
                (int)res.StatusCode,
                responseBody);

            using var document = JsonDocument.Parse(responseBody);
            var body = document.RootElement;

            if (res.IsSuccessStatusCode && body.TryGetProperty("error", out var err) && err.GetInt32() == 0)
            {
                var msgId = body.TryGetProperty("data", out var d)
                    && d.TryGetProperty("msg_id", out var mid) ? mid.GetString() : null;
                _logger.LogInformation(
                    "ZNS sent to {Phone} template {TemplateId}: msg_id={MsgId}",
                    znsPhone,
                    templateId,
                    msgId);
                return new ZaloSendResult { Success = true, MessageId = msgId };
            }

            var errMsg = body.TryGetProperty("message", out var m) ? m.GetString() : res.StatusCode.ToString();
            _logger.LogWarning(
                "ZNS failed for {Phone}: {Error}",
                znsPhone,
                errMsg);
            return new ZaloSendResult { Success = false, Error = errMsg };
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "ZNS exception for phone {Phone} template {TemplateId}",
                phone,
                templateId);
            return new ZaloSendResult { Success = false, Error = ex.Message };
        }
    }

    private static string NormalizeVietnamPhoneForZns(string phone)
    {
        var digits = new string(phone.Where(char.IsDigit).ToArray());

        if (digits.StartsWith("84", StringComparison.Ordinal))
        {
            return digits;
        }

        if (digits.StartsWith('0'))
        {
            return $"84{digits[1..]}";
        }

        if (digits.Length is 9 or 10)
        {
            return $"84{digits}";
        }

        throw new ArgumentException("Số điện thoại không đúng định dạng Việt Nam.", nameof(phone));
    }

    // ─── User Link Status ────────────────────────────────────────────────────

    public async Task<bool> IsZaloLinkedAsync(string userId)
    {
        using var scope = _serviceProvider.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var user = await unitOfWork.UserRepository.GetUserByIdAsync(userId);
        return !string.IsNullOrEmpty(user?.Zalouserid);
    }
}

public class ZaloOAConfig
{
    public bool MockMode { get; set; } = true;
    public string? AppId { get; set; }
    public string? SecretKey { get; set; }       // For webhook MAC verification
    public string? AppSecretKey { get; set; }    // For OAuth token API (secret_key header)
    public string? OAId { get; set; }
    public string? RefreshToken { get; set; }

    // ZNS Template IDs — điền sau khi Zalo duyệt template
    public string? ZnsTemplateOtp { get; set; }
    public string? ZnsTemplateLessonReminder { get; set; }
    public string? ZnsTemplateBookingConfirmed { get; set; }
    public string? ZnsTemplateLessonReport { get; set; }
    public string? ZnsTemplatePayoutProcessed { get; set; }
}
