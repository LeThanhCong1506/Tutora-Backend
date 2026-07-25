using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MV.ApplicationLayer.Interfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO;
using MV.PresentationLayer.Authorization;

namespace MV.PresentationLayer.Controllers;

/// <summary>
/// Tells staff whether the Agora notification feed is actually usable as evidence, without anyone
/// having to open a SQL console.
///
/// The whole attendance report hangs on one unknown: whether a real notification carries the
/// <c>account</c> field. With it, every Agora uid binds to a user exactly. Without it, uids can only
/// be matched by admission timing, which marks sessions "identity uncertain" and blocks the
/// no-show and refund guidance. This endpoint answers that from the events already stored, so the
/// question can be settled by running one real lesson and refreshing a page.
/// </summary>
[ApiController]
[Route("api/admin/agora")]
[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class AdminAgoraDiagnosticsController(IAppDbContext context) : ControllerBase
{
    /// <summary>Events inspected per request. Enough for several lessons, cheap enough to be instant.</summary>
    private const int SampleSize = 500;

    /// <summary>
    /// GET /api/admin/agora/ncs-diagnostics
    /// Field coverage across the most recent stored notifications: how many join/leave events
    /// arrived, how many carried <c>account</c>, and what shape <c>uid</c> came in.
    /// </summary>
    [HttpGet("ncs-diagnostics")]
    [RequirePermission(Permissions.DisputeView)]
    public async Task<IActionResult> GetNcsDiagnostics(CancellationToken cancellationToken)
    {
        var recent = await context.AgoraChannelEvents
            .AsNoTracking()
            .OrderByDescending(e => e.EventId)
            .Take(SampleSize)
            .Select(e => new { e.EventType, e.EventAt, e.ReceivedAt, e.ClassSessionId, e.Payload })
            .ToListAsync(cancellationToken);

        var participantEvents = 0;
        var withAccount = 0;
        var withUid = 0;
        var numericUid = 0;
        var stringUid = 0;
        var unreadablePayloads = 0;
        var sessionIds = new HashSet<int>();
        var payloadKeys = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var row in recent)
        {
            if (row.ClassSessionId.HasValue)
                sessionIds.Add(row.ClassSessionId.Value);

            JsonElement payload;
            try
            {
                using var document = JsonDocument.Parse(row.Payload);
                payload = document.RootElement.Clone();
            }
            catch (JsonException)
            {
                unreadablePayloads += 1;
                continue;
            }

            if (payload.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in payload.EnumerateObject())
                    payloadKeys.Add(property.Name);
            }

            var isParticipantEvent = AgoraChannelEventType.IsJoin(row.EventType)
                                     || AgoraChannelEventType.IsLeave(row.EventType);
            if (!isParticipantEvent)
                continue;

            participantEvents += 1;

            if (payload.TryGetProperty("account", out var account)
                && account.ValueKind is JsonValueKind.String or JsonValueKind.Number
                && !string.IsNullOrWhiteSpace(account.ToString()))
            {
                withAccount += 1;
            }

            if (payload.TryGetProperty("uid", out var uid))
            {
                switch (uid.ValueKind)
                {
                    case JsonValueKind.Number:
                        withUid += 1;
                        numericUid += 1;
                        break;
                    case JsonValueKind.String when !string.IsNullOrWhiteSpace(uid.GetString()):
                        withUid += 1;
                        stringUid += 1;
                        break;
                }
            }
        }

        // Stated as a recommendation rather than a bare number: the point of the probe is a decision
        // about whether identity binding can be trusted, and that decision is the same every time.
        var verdict = recent.Count == 0
            ? "Chưa nhận được sự kiện nào từ Agora. Hãy chạy một buổi học thật rồi kiểm tra lại — nếu vẫn trống, webhook NCS chưa trỏ đúng về hệ thống."
            : participantEvents == 0
                ? "Có sự kiện kênh nhưng chưa có sự kiện vào/rời phòng nào. Cần một buổi có người thật vào phòng để kết luận."
                : withAccount == participantEvents
                    ? "Mọi sự kiện vào/rời đều có trường 'account' — danh tính ghép được CHÍNH XÁC, nhật ký buổi học đủ sức kết luận."
                    : withAccount == 0
                        ? "KHÔNG sự kiện nào có trường 'account'. Danh tính chỉ ghép được theo thời điểm vào phòng, nên phần lớn buổi sẽ bị gắn cờ 'identity_uncertain' và không đưa ra kết luận vắng mặt / hoàn tiền."
                        : "Chỉ một phần sự kiện có trường 'account'. Những buổi thiếu trường này sẽ phải ghép danh tính theo thời điểm và bị gắn cờ 'identity_uncertain'.";

        return Ok(APIResponse<object>.Success(
            new
            {
                sampledEvents = recent.Count,
                sampleLimit = SampleSize,
                distinctSessions = sessionIds.Count,
                participantEvents,
                eventsWithAccount = withAccount,
                eventsWithUid = withUid,
                numericUidEvents = numericUid,
                stringUidEvents = stringUid,
                unreadablePayloads,
                payloadKeys = payloadKeys.ToList(),
                firstEventAt = recent.Count > 0 ? recent.Min(e => e.EventAt) : (DateTime?)null,
                lastEventAt = recent.Count > 0 ? recent.Max(e => e.EventAt) : (DateTime?)null,
                lastReceivedAt = recent.Count > 0 ? recent.Max(e => e.ReceivedAt) : (DateTime?)null,
                verdict
            },
            "Lấy thông tin chẩn đoán webhook Agora thành công."));
    }
}
