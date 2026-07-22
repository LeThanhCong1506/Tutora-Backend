using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MV.ApplicationLayer.Interfaces;
using MV.DomainLayer.Configuration;
using MV.DomainLayer.Entities;
using MV.DomainLayer.Helpers;
using Npgsql;

namespace MV.PresentationLayer.Controllers;

/// <summary>Receives Agora Notification Center Service callbacks.</summary>
[ApiController]
[Route("api/webhooks/agora")]
public class AgoraWebhookController : ControllerBase
{
    private const string SignatureHeader = "Agora-Signature-V2";
    private const string NoticeIdUniqueConstraint = "ux_agora_events_notice";

    private readonly IAppDbContext _context;
    private readonly ILogger<AgoraWebhookController> _logger;
    private readonly AgoraNotificationSettings _settings;
    private readonly IHostEnvironment _environment;

    public AgoraWebhookController(
        IAppDbContext context,
        ILogger<AgoraWebhookController> logger,
        IOptions<AgoraNotificationSettings> settings,
        IHostEnvironment environment)
    {
        _context = context;
        _logger = logger;
        _settings = settings.Value;
        _environment = environment;
    }

    [HttpPost("ncs")]
    [AllowAnonymous]
    public async Task<IActionResult> HandleNcsWebhook()
    {
        using var bodyBuffer = new MemoryStream();
        await Request.Body.CopyToAsync(bodyBuffer, HttpContext.RequestAborted);
        var rawBodyBytes = bodyBuffer.ToArray();
        bodyBuffer.Position = 0;

        using var reader = new StreamReader(
            bodyBuffer,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true,
            leaveOpen: false);
        var rawBody = await reader.ReadToEndAsync(HttpContext.RequestAborted);

        // Rows here decide refunds and tutor penalties, so unsigned writes must never be accepted
        // outside local development — an unauthenticated endpoint with verification off would let
        // anyone who knows the URL forge session evidence.
        var verificationConfigured = _settings.Enabled && !string.IsNullOrWhiteSpace(_settings.Secret);
        if (!verificationConfigured)
        {
            if (!_environment.IsDevelopment())
            {
                _logger.LogError(
                    "Rejecting Agora NCS webhook: signature verification is not configured outside Development. "
                    + "Enabled={Enabled}, SecretConfigured={SecretConfigured}.",
                    _settings.Enabled,
                    !string.IsNullOrWhiteSpace(_settings.Secret));
                return Unauthorized(new { error = "verification_not_configured" });
            }

            _logger.LogWarning(
                "Agora NCS signature verification is disabled (Development only). Enabled={Enabled}, SecretConfigured={SecretConfigured}.",
                _settings.Enabled,
                !string.IsNullOrWhiteSpace(_settings.Secret));
        }
        else
        {
            var receivedSignature = Request.Headers[SignatureHeader].FirstOrDefault();
            if (!VerifySignature(rawBodyBytes, receivedSignature, _settings.Secret))
            {
                _logger.LogWarning(
                    "Agora NCS signature verification failed. PayloadLength={PayloadLength}.",
                    rawBody.Length);
                return Unauthorized(new { error = "invalid_signature" });
            }
        }

        ParsedNotification notification;
        try
        {
            using var document = JsonDocument.Parse(rawBody);
            var root = document.RootElement;
            var productId = GetRequiredInt32(root, "productId");

            if (productId != 1)
            {
                _logger.LogDebug("Ignoring non-RTC Agora NCS event. ProductId={ProductId}.", productId);
                return Acknowledge();
            }

            notification = ParseRtcNotification(root);
        }
        catch (JsonException ex)
        {
            // Agora retries three times then drops the event for good, so a payload shape this
            // probe did not anticipate is lost unless the body itself is in the log. The whole
            // point of the probe is discovering that shape — log it verbatim.
            _logger.LogError(
                ex,
                "Failed to parse Agora NCS webhook payload. RawBody={RawBody}",
                rawBody);
            return Acknowledge();
        }

        var channelEvent = new AgoraChannelEvent
        {
            NoticeId = notification.NoticeId,
            ClassSessionId = notification.ClassSessionId,
            EventType = notification.EventType,
            EventAt = notification.EventAt,
            ReceivedAt = TimeZoneHelper.UtcNow,
            Payload = notification.Payload
        };

        _context.AgoraChannelEvents.Add(channelEvent);

        try
        {
            await _context.SaveChangesAsync(HttpContext.RequestAborted);
        }
        catch (DbUpdateException ex) when (IsDuplicateNoticeId(ex))
        {
            _logger.LogInformation(
                "Ignoring duplicate Agora NCS event. NoticeId={NoticeId}.",
                notification.NoticeId);
            return Acknowledge();
        }

        _logger.LogInformation(
            "Stored Agora NCS event. NoticeId={NoticeId}, EventType={EventType}, ClassSessionId={ClassSessionId}.",
            notification.NoticeId,
            notification.EventType,
            notification.ClassSessionId);

        return Acknowledge();
    }

    private OkObjectResult Acknowledge() => Ok(new { success = true });

    private static ParsedNotification ParseRtcNotification(JsonElement root)
    {
        var noticeId = GetRequiredString(root, "noticeId");
        if (noticeId.Length > 64)
            throw new JsonException("Agora NCS noticeId exceeds 64 characters.");

        var eventTypeValue = GetRequiredInt32(root, "eventType");
        if (eventTypeValue is < short.MinValue or > short.MaxValue)
            throw new JsonException("Agora NCS eventType is outside the SMALLINT range.");

        if (!root.TryGetProperty("payload", out var payload) || payload.ValueKind != JsonValueKind.Object)
            throw new JsonException("Agora NCS payload must be a JSON object.");

        var timestamp = GetRequiredInt64(payload, "ts");
        DateTime eventAt;
        try
        {
            eventAt = DateTimeOffset.FromUnixTimeSeconds(timestamp).UtcDateTime;
        }
        catch (ArgumentOutOfRangeException ex)
        {
            throw new JsonException("Agora NCS payload.ts is outside the Unix timestamp range.", ex);
        }

        int? classSessionId = null;
        if (payload.TryGetProperty("channelName", out var channelNameElement))
        {
            var channelName = channelNameElement.ValueKind switch
            {
                JsonValueKind.String => channelNameElement.GetString(),
                JsonValueKind.Number => channelNameElement.GetRawText(),
                _ => null
            };

            if (int.TryParse(channelName, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedId))
                classSessionId = parsedId;
        }

        return new ParsedNotification(
            noticeId,
            (short)eventTypeValue,
            classSessionId,
            eventAt,
            payload.GetRawText());
    }

    private static string GetRequiredString(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
            throw new JsonException($"Agora NCS {propertyName} must be a string.");

        var value = property.GetString();
        if (string.IsNullOrWhiteSpace(value))
            throw new JsonException($"Agora NCS {propertyName} is required.");

        return value;
    }

    private static int GetRequiredInt32(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.Number ||
            !property.TryGetInt32(out var value))
        {
            throw new JsonException($"Agora NCS {propertyName} must be an integer.");
        }

        return value;
    }

    private static long GetRequiredInt64(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.Number ||
            !property.TryGetInt64(out var value))
        {
            throw new JsonException($"Agora NCS {propertyName} must be an integer.");
        }

        return value;
    }

    private static bool VerifySignature(byte[] rawBody, string? receivedSignature, string secret)
    {
        if (string.IsNullOrWhiteSpace(receivedSignature))
            return false;

        byte[] receivedHash;
        try
        {
            receivedHash = Convert.FromHexString(receivedSignature.Trim());
        }
        catch (FormatException)
        {
            return false;
        }

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var expectedHash = hmac.ComputeHash(rawBody);
        return receivedHash.Length == expectedHash.Length &&
               CryptographicOperations.FixedTimeEquals(receivedHash, expectedHash);
    }

    private static bool IsDuplicateNoticeId(DbUpdateException exception)
    {
        if (exception.InnerException is PostgresException postgresException &&
            postgresException.SqlState == PostgresErrorCodes.UniqueViolation &&
            string.Equals(
                postgresException.ConstraintName,
                NoticeIdUniqueConstraint,
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var message = $"{exception.Message} {exception.InnerException?.Message}";
        return message.Contains(NoticeIdUniqueConstraint, StringComparison.OrdinalIgnoreCase);
    }

    private sealed record ParsedNotification(
        string NoticeId,
        short EventType,
        int? ClassSessionId,
        DateTime EventAt,
        string Payload);
}
