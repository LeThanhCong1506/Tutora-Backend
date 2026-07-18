using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.Helpers;
using MV.ApplicationLayer.Interfaces;
using MV.ApplicationLayer.RepositoryInterfaces;
using System.Text.Json;
using static MV.DomainLayer.Constants.ClassSessionStatus;

namespace MV.ApplicationLayer.Services;

/// <summary>
/// Service for admin dispute management
/// </summary>
public class DisputeService : IDisputeService
{
    private readonly IDisputeRepository _disputeRepo;
    private readonly IAppDbContext _context; // retained for transaction management only
    private readonly ISettlementService _settlementService;
    private readonly IWarningService _warningService;
    private readonly INotificationService _notificationService;
    private readonly ILogger<DisputeService> _logger;

    public DisputeService(
        IDisputeRepository disputeRepo,
        IAppDbContext context,
        ISettlementService settlementService,
        IWarningService warningService,
        INotificationService notificationService,
        ILogger<DisputeService> logger)
    {
        _disputeRepo = disputeRepo;
        _context = context;
        _settlementService = settlementService;
        _warningService = warningService;
        _notificationService = notificationService;
        _logger = logger;
    }

    public async Task<PagedList<DisputeListResponse>> GetDisputesAsync(DisputeQueryRequest query)
    {
        var q = _disputeRepo.GetBaseQuery();

        if (!string.IsNullOrWhiteSpace(query.Status))
            q = q.Where(d => d.Status == query.Status);
        if (query.StartDate.HasValue)
        {
            var startUtc = query.StartDate.Value.Kind == DateTimeKind.Utc
                ? query.StartDate.Value
                : DateTime.SpecifyKind(query.StartDate.Value, DateTimeKind.Utc);
            q = q.Where(d => d.Createdat >= startUtc);
        }
        if (query.EndDate.HasValue)
        {
            var endUtc = query.EndDate.Value.Kind == DateTimeKind.Utc
                ? query.EndDate.Value
                : DateTime.SpecifyKind(query.EndDate.Value, DateTimeKind.Utc);
            q = q.Where(d => d.Createdat <= endUtc);
        }
        if (!string.IsNullOrWhiteSpace(query.DisputeType))
            q = q.Where(d => d.Disputetype == query.DisputeType);

        q = q.OrderByDescending(d => d.Createdat);

        var totalCount = await q.CountAsync();
        var rawDisputes = await q
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(d => new
            {
                d.Disputeid,
                d.Classsessionid,
                d.Bookingid,
                d.Disputetype,
                d.Status,
                d.Reason,
                CreatedByName = d.CreatedbyNavigation!.Fullname,
                TutorName = d.ClassSession!.Tutor!.Tutor!.Fullname,
                ClassSessionPrice = d.ClassSession.Lessonprice,
                d.Createdat
            })
            .ToListAsync();

        var disputes = rawDisputes.Select(d => new DisputeListResponse
        {
            DisputeId = d.Disputeid,
            ClassSessionId = d.Classsessionid,
            BookingId = d.Bookingid,
            DisputeType = d.Disputetype,
            Status = d.Status,
            Reason = d.Reason,
            CreatedByName = d.CreatedByName,
            TutorName = d.TutorName,
            ClassSessionPrice = d.ClassSessionPrice,
            CreatedAt = d.Createdat.HasValue ? d.Createdat.Value : (DateTime?)null
        }).ToList();

        return new PagedList<DisputeListResponse>(disputes, totalCount, query.Page, query.PageSize);
    }

    public async Task<DisputeDetailResponse?> GetDisputeDetailAsync(int disputeId)
    {
        var dispute = await _disputeRepo.GetDetailAsync(disputeId);
        if (dispute == null) return null;

        var warningCount = await _disputeRepo.CountWarningsByTutorAsync(dispute.ClassSession!.Tutorid!);

        var (recordingStatus, recordingUrl) = ResolveRecordingStatus(
            dispute.ClassSession?.Recordingurl, dispute.ClassSession?.Recordings3key, dispute.ClassSession?.Recordingsid);

        return new DisputeDetailResponse
        {
            DisputeId = dispute.Disputeid,
            BookingId = dispute.Bookingid,
            ClassSessionId = dispute.Classsessionid,
            DisputeType = dispute.Disputetype,
            Reason = dispute.Reason,
            Status = dispute.Status,
            Evidence = DeserializeJsonList(dispute.Evidence),
            CreatedAt = dispute.Createdat.HasValue ? dispute.Createdat.Value : (DateTime?)null,
            ResolvedAt = dispute.Resolvedat.HasValue ? dispute.Resolvedat.Value : (DateTime?)null,
            ResolutionNote = dispute.Resolutionnote,
            RefundAmount = dispute.Refundamount,
            RefundPercentage = dispute.Refundpercentage,
            CreatedBy = dispute.CreatedbyNavigation != null ? new DisputeUserResponse
            {
                UserId = dispute.Createdby,
                FullName = dispute.CreatedbyNavigation.Fullname,
                Email = dispute.CreatedbyNavigation.Email,
                Phone = dispute.CreatedbyNavigation.Phone
            } : null,
            ResolvedBy = dispute.ResolvedbyNavigation != null ? new DisputeUserResponse
            {
                UserId = dispute.Resolvedby,
                FullName = dispute.ResolvedbyNavigation.Fullname,
                Email = dispute.ResolvedbyNavigation.Email
            } : null,
            ClassSession = dispute.ClassSession != null ? new DisputeClassSessionResponse
            {
                ClassSessionId = dispute.ClassSession.Classsessionid,
                ScheduledStart = dispute.ClassSession.Scheduledstart,
                ScheduledEnd = dispute.ClassSession.Scheduledend,
                Status = dispute.ClassSession.Status,
                ClassSessionPrice = dispute.ClassSession.Lessonprice,
                ClassSessionContent = dispute.ClassSession.Lessoncontent,
                Homework = dispute.ClassSession.Homework,
                IsTutorPresent = dispute.ClassSession.Istutorpresent,
                IsStudentPresent = dispute.ClassSession.Isstudentpresent,
                RecordingStatus = recordingStatus,
                RecordingUrl = recordingUrl
            } : null,
            Tutor = dispute.ClassSession?.Tutor?.Tutor != null ? new DisputeTutorResponse
            {
                TutorId = dispute.ClassSession.Tutorid,
                FullName = dispute.ClassSession.Tutor.Tutor.Fullname,
                Email = dispute.ClassSession.Tutor.Tutor.Email,
                Phone = dispute.ClassSession.Tutor.Tutor.Phone,
                WarningCount = warningCount,
                AverageRating = dispute.ClassSession.Tutor.Averagerating.HasValue ? (decimal?)dispute.ClassSession.Tutor.Averagerating.Value : null
            } : null
        };
    }

    public async Task<DisputeRecordingResponse> GetDisputeRecordingAsync(int disputeId)
    {
        var dispute = await _disputeRepo.FindWithClassSessionAsync(disputeId)
            ?? throw new ArgumentException("Không tìm thấy tranh chấp");

        var cs = dispute.ClassSession;
        var (status, url) = ResolveRecordingStatus(cs?.Recordingurl, cs?.Recordings3key, cs?.Recordingsid);

        return new DisputeRecordingResponse
        {
            DisputeId = dispute.Disputeid,
            ClassSessionId = dispute.Classsessionid,
            Status = status,
            RecordingUrl = url,
            Available = url != null
        };
    }

    /// <summary>
    /// Suy ra trạng thái + link recording từ các cột buổi học (không có cột status riêng):
    /// có url → available; còn s3key → processing (đang relay lên Drive); còn sid → recording (đang ghi); còn lại none.
    /// </summary>
    private static (string status, string? url) ResolveRecordingStatus(string? url, string? s3key, string? sid)
    {
        if (!string.IsNullOrEmpty(url)) return ("available", url);
        if (!string.IsNullOrEmpty(s3key)) return ("processing", null);
        if (!string.IsNullOrEmpty(sid)) return ("recording", null);
        return ("none", null);
    }

    public async Task<List<ChatMessageResponse>> GetDisputeChatHistoryAsync(int disputeId)
    {
        var dispute = await _disputeRepo.FindWithBookingAsync(disputeId);
        if (dispute?.Booking == null) return new List<ChatMessageResponse>();

        var channelId = await _disputeRepo.GetChannelIdForBookingAsync(dispute.Bookingid ?? 0);
        if (channelId == null) return new List<ChatMessageResponse>();

        var messages = await _disputeRepo.GetChannelMessagesAsync(channelId.Value);

        return messages.Select(m => new ChatMessageResponse
        {
            MessageId = m.Messageid,
            ChannelId = m.Channelid ?? 0,
            SenderId = m.Senderid ?? string.Empty,
            Content = m.Content ?? string.Empty,
            MessageType = m.Messagetype ?? ChatMessageType.Text,
            CreatedAt = m.Createdat.HasValue ? m.Createdat.Value : (DateTime?)null
        }).ToList();
    }

    public async Task<DisputeDetailResponse> InvestigateDisputeAsync(int disputeId, string adminId)
    {
        var dispute = await _disputeRepo.FindWithClassSessionAsync(disputeId)
            ?? throw new ArgumentException("Không tìm thấy tranh chấp");

        if (dispute.Status != DisputeStatus.Pending)
            throw new InvalidOperationException("Tranh chấp chưa ở trạng thái chờ xử lý");

        dispute.Status = DisputeStatus.Investigating;
        await _disputeRepo.SaveChangesAsync();

        _logger.LogInformation("Actor {ActorId} started investigating dispute {DisputeId}", adminId, disputeId);

        return (await GetDisputeDetailAsync(disputeId))!;
    }

    public async Task<DisputeDetailResponse> ResolveDisputeAsync(int disputeId, string adminId, ResolveDisputeRequest request)
    {
        if (!ResolutionTypes.All.Contains(request.ResolutionType))
            throw new ArgumentException("Loại kết quả xử lý không hợp lệ");

        var dispute = await _disputeRepo.FindWithClassSessionAsync(disputeId)
            ?? throw new ArgumentException("Không tìm thấy tranh chấp");

        if (dispute.Status == DisputeStatus.Resolved || dispute.Status == DisputeStatus.Closed)
            throw new InvalidOperationException("Tranh chấp này đã được giải quyết rồi");

        await using var tx = await _context.Database.BeginTransactionAsync();
        try
        {
            var now = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;
            var classSessionId = dispute.Classsessionid ?? 0;
            var tutorId = dispute.ClassSession?.Tutorid;

            var refundPercentage = request.ResolutionType switch
            {
                ResolutionTypes.Release => 0,
                ResolutionTypes.Refund50 => 50,
                ResolutionTypes.Refund100 => 100,
                _ => 0
            };

            if (classSessionId > 0)
            {
                if (refundPercentage > 0)
                    await _settlementService.ProcessRefundAsync(classSessionId, refundPercentage, adminId);
                else
                    // Release (side with tutor): settle even though the classSession is Disputed/NoShow.
                    await _settlementService.SettleDisputedClassSessionAsync(classSessionId, adminId);
            }

            dispute.Status = DisputeStatus.Resolved;
            dispute.Resolvedat = now;
            dispute.Resolvedby = adminId;
            dispute.Resolutionnote = request.ResolutionNote;
            dispute.Refundpercentage = refundPercentage;

            if (request.CreateTutorWarning && tutorId != null)
            {
                var warningRequest = new CreateWarningRequest
                {
                    WarningLevel = request.WarningLevel ?? 1,
                    Reason = $"Dispute resolved against tutor: {request.ResolutionNote}",
                    RelatedBookingId = dispute.Bookingid
                };
                await _warningService.CreateWarningAsync(tutorId, warningRequest, adminId);
            }

            if (dispute.ClassSession != null)
                dispute.ClassSession.Status = refundPercentage == 100 ? Cancelled : Completed;

            await _disputeRepo.SaveChangesAsync();
            await tx.CommitAsync();

            _logger.LogInformation("Actor {ActorId} resolved dispute {DisputeId} with {Resolution}",
                adminId, disputeId, request.ResolutionType);

            var notifications = new List<NotificationRequest>
            {
                new() { Userid = dispute.Createdby, Title = "Tranh chấp đã được giải quyết",
                    Message = $"Tranh chấp #{disputeId} đã được giải quyết. Kết quả: {request.ResolutionType}. Ghi chú: {request.ResolutionNote}" }
            };

            if (tutorId != null)
                notifications.Add(new() { Userid = tutorId, Title = "Thông báo giải quyết tranh chấp",
                    Message = $"Tranh chấp #{disputeId} liên quan đến buổi học của bạn đã được giải quyết. Kết quả: {request.ResolutionType}." });

            await _notificationService.CreateNotificationsAsync(notifications);

            return (await GetDisputeDetailAsync(disputeId))!;
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    public async Task<DisputeStatsResponse> GetDisputeStatsAsync()
    {
        var now = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;
        var startOfMonth = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        return new DisputeStatsResponse
        {
            TotalPending = await _disputeRepo.CountByStatusAsync(DisputeStatus.Pending),
            TotalInvestigating = await _disputeRepo.CountByStatusAsync(DisputeStatus.Investigating),
            ResolvedThisMonth = await _disputeRepo.CountResolvedSinceAsync(startOfMonth),
            TotalRefundedThisMonth = await _disputeRepo.SumRefundedSinceAsync(startOfMonth)
        };
    }

    private static List<string>? DeserializeJsonList(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (value.TrimStart().StartsWith('['))
        {
            try { return JsonSerializer.Deserialize<List<string>>(value); }
            catch { }
        }
        return value.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
    }
}
