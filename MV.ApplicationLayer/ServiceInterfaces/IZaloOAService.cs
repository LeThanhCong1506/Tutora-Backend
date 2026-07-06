using MV.DomainLayer.DTO.ResponseModel.Zalo;

namespace MV.ApplicationLayer.ServiceInterfaces;

public interface IZaloOAService
{
    /// <summary>
    /// Send an OTP through ZNS using the verified phone number.
    /// </summary>
    Task<ZaloSendResult> SendZnsOtpAsync(string phone, string otp);

    /// <summary>
    /// Send a post-classSession report notification to the student's parent via Zalo OA template.
    /// </summary>
    Task<ZaloSendResult> SendClassSessionReportAsync(int classSessionId);

    /// <summary>
    /// Send a generic Zalo OA template message to a user with dynamic data fields.
    /// </summary>
    Task<ZaloSendResult> SendNotificationAsync(string userId, string templateId, Dictionary<string, string> data);

    /// <summary>
    /// Process an inbound Zalo OA webhook event (follow / unfollow / user_send_text).
    /// </summary>
    Task HandleWebhookAsync(string payload);

    /// <summary>
    /// Check whether a user has linked their Zalo account.
    /// </summary>
    Task<bool> IsZaloLinkedAsync(string userId);

    // ── Token management ──────────────────────────────────────────────────

    /// <summary>
    /// Return a valid Zalo OA access token, refreshing if necessary.
    /// </summary>
    Task<string> GetOAAccessTokenAsync();

    /// <summary>
    /// Proactively refresh the access token if it is missing or about to expire.
    /// Called by a background job so the token never lapses between the lazy
    /// refresh points (the bot only reads the token, it never triggers a refresh).
    /// </summary>
    Task EnsureFreshTokenAsync();

    // ── OA Reply API ──────────────────────────────────────────────────────

    /// <summary>
    /// Send a plain text reply to a Zalo user from the OA account.
    /// </summary>
    Task SendOAMessageAsync(string recipientZaloId, string text);

    /// <summary>
    /// Send a text message with quick-reply buttons to a Zalo user from the OA account.
    /// </summary>
    Task SendOAMessageWithButtonsAsync(string recipientZaloId, string text, List<ZaloQuickReply> buttons);
}
