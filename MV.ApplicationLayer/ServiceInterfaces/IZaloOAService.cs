using MV.DomainLayer.DTO.ResponseModel.Zalo;

namespace MV.ApplicationLayer.ServiceInterfaces;

public interface IZaloOAService
{
    /// <summary>
    /// Send a post-lesson report notification to the student's parent via Zalo OA template.
    /// </summary>
    Task<ZaloSendResult> SendLessonReportAsync(int lessonId);

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
