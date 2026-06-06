using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.ApplicationLayer.Services;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MV.PresentationLayer.Controllers;

/// <summary>
/// Handles webhook callbacks from Zalo OA.
/// Zalo POSTs events to this endpoint (follow, unfollow, user_send_text, etc.)
/// Must return HTTP 200 quickly — process async if needed.
/// </summary>
[Route("api/webhooks")]
[ApiController]
public class ZaloWebhookController : ControllerBase
{
    private readonly IZaloOAService _zaloOAService;
    private readonly ILogger<ZaloWebhookController> _logger;
    private readonly ZaloOAConfig _config;

    public ZaloWebhookController(
        IZaloOAService zaloOAService,
        ILogger<ZaloWebhookController> logger,
        IOptions<ZaloOAConfig> config)
    {
        _zaloOAService = zaloOAService;
        _logger = logger;
        _config = config.Value;
    }

    /// <summary>
    /// Receives Zalo OA webhook events.
    /// Docs: https://developers.zalo.me/docs/official-account/webhook
    /// </summary>
    [HttpPost("zalo")]
    [AllowAnonymous]  // Zalo does not send auth headers
    public async Task<IActionResult> HandleZaloWebhook()
    {
        // Read raw body for MAC verification
        using var reader = new StreamReader(Request.Body, Encoding.UTF8);
        var rawBody = await reader.ReadToEndAsync();

        if (string.IsNullOrEmpty(rawBody))
        {
            _logger.LogWarning("Zalo webhook received empty body.");
            return Ok();
        }

        // Verify MAC signature (skip if SecretKey empty or MockMode)
        // Zalo sends mac inside JSON body, not as HTTP header
        if (!_config.MockMode && !string.IsNullOrEmpty(_config.SecretKey))
        {
            try
            {
                using var tempDoc = JsonDocument.Parse(rawBody);
                var root2 = tempDoc.RootElement;
                var bodyMac = root2.TryGetProperty("mac", out var macEl) ? macEl.GetString() : null;
                var appId = root2.TryGetProperty("app_id", out var apEl) ? apEl.GetString() : null;
                var userId = root2.TryGetProperty("user_id_by_app", out var uEl) ? uEl.GetString() : null;
                var timestamp = root2.TryGetProperty("timestamp", out var tsEl) ? tsEl.GetString() : null;

                // Only verify if Zalo actually sent a mac field
                if (!string.IsNullOrEmpty(bodyMac))
                {
                    var dataToSign = $"app_id={appId}&user_id={userId}&timestamp={timestamp}";
                    if (!VerifyMac(dataToSign, bodyMac, _config.SecretKey))
                    {
                        _logger.LogWarning("Zalo webhook MAC verification failed. data={Data}, mac={Mac}", dataToSign, bodyMac);
                        return Unauthorized();
                    }
                }
            }
            catch (JsonException)
            {
                return Unauthorized();
            }
        }

        _logger.LogInformation("Zalo webhook received: {Body}", rawBody[..Math.Min(200, rawBody.Length)]);

        // Parse event type for routing
        try
        {
            using var doc = JsonDocument.Parse(rawBody);
            var root = doc.RootElement;
            var eventName = root.TryGetProperty("event_name", out var ev) ? ev.GetString() : null;

            _logger.LogInformation("Zalo webhook event: {EventName}.", eventName);

            // Delegate to ZaloOAService for full handling
            await _zaloOAService.HandleWebhookAsync(rawBody);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse Zalo webhook payload.");
        }

        // Always return 200 to Zalo (even on processing errors)
        return Ok();
    }

    /// <summary>
    /// Verify Zalo OA webhook MAC signature.
    /// Zalo signs: HMAC-SHA256(rawBody, OASecretKey)
    /// </summary>
    private static bool VerifyMac(string rawBody, string? receivedMac, string secretKey)
    {
        if (string.IsNullOrEmpty(receivedMac)) return false;

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secretKey));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(rawBody));
        var expected = Convert.ToHexString(hash).ToLowerInvariant();
        return string.Equals(expected, receivedMac.ToLowerInvariant(), StringComparison.Ordinal);
    }
}
