using Hangfire;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MV.ApplicationLayer.Hubs;
using MV.ApplicationLayer.Helpers;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.Entities;
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
    private static readonly TimeSpan RecordingTokenLifetime = TimeSpan.FromMinutes(15);

    private readonly IDisputeRepository _disputeRepo;
    private readonly IAppDbContext _context; // retained for transaction management only
    private readonly ISettlementService _settlementService;
    private readonly IWarningService _warningService;
    private readonly INotificationService _notificationService;
    private readonly IFileStorageService _storageService;
    private readonly IHubContext<NotificationHub> _hubContext;
    private readonly IDisputeClassificationService _classificationService;
    private readonly IRecordingAccessTokenService _recordingAccessTokenService;
    private readonly IBackgroundJobClient _backgroundJobClient;
    private readonly ILogger<DisputeService> _logger;

    public DisputeService(
        IDisputeRepository disputeRepo,
        IAppDbContext context,
        ISettlementService settlementService,
        IWarningService warningService,
        INotificationService notificationService,
        IFileStorageService storageService,
        IHubContext<NotificationHub> hubContext,
        IDisputeClassificationService classificationService,
        IRecordingAccessTokenService recordingAccessTokenService,
        IBackgroundJobClient backgroundJobClient,
        ILogger<DisputeService> logger)
    {
        _disputeRepo = disputeRepo;
        _context = context;
        _settlementService = settlementService;
        _warningService = warningService;
        _notificationService = notificationService;
        _storageService = storageService;
        _hubContext = hubContext;
        _classificationService = classificationService;
        _recordingAccessTokenService = recordingAccessTokenService;
        _backgroundJobClient = backgroundJobClient;
        _logger = logger;
    }

    public async Task<PagedList<DisputeListResponse>> GetDisputesAsync(DisputeQueryRequest query)
    {
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 50);
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
        if (query.ClassSessionId.HasValue)
            q = q.Where(d => d.Classsessionid == query.ClassSessionId.Value);

        q = q.ApplyDisputeSearch(query.Search, includeParticipantNames: true);

        var totalCount = await q.CountAsync();
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
        page = Math.Min(page, totalPages);

        var rawDisputes = await q
            .OrderForDisputeList(query.SortDirection)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(d => new
            {
                d.Disputeid,
                d.Classsessionid,
                d.Bookingid,
                d.Disputetype,
                d.Status,
                d.Reason,
                d.Priority,
                d.Priorityreason,
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
            Priority = d.Priority,
            PriorityReason = d.Priorityreason,
            CreatedByName = d.CreatedByName,
            TutorName = d.TutorName,
            ClassSessionPrice = d.ClassSessionPrice,
            CreatedAt = d.Createdat.HasValue ? d.Createdat.Value : (DateTime?)null
        }).ToList();

        return new PagedList<DisputeListResponse>(disputes, totalCount, page, pageSize);
    }

    public async Task<DisputeDetailResponse?> GetDisputeDetailAsync(int disputeId, string actorId)
    {
        var dispute = await _disputeRepo.GetDetailAsync(disputeId);
        if (dispute == null) return null;

        var warningCount = await _disputeRepo.CountWarningsByTutorAsync(dispute.ClassSession!.Tutorid!);

        var (recordingStatus, recordingUrl) = RecordingStatusResolver.Resolve(
            dispute.ClassSession?.Recordingurl, dispute.ClassSession?.Recordings3key, dispute.ClassSession?.Recordingsid,
            dispute.ClassSession?.Checkouttime.HasValue ?? false);
        var recordingStreamUrl = BuildRecordingStreamUrl(dispute.Classsessionid, actorId, recordingUrl);

        var scheduleChanges = dispute.Classsessionid.HasValue
            ? await _context.ClassSessionScheduleChanges.AsNoTracking()
                .Where(x => x.Classsessionid == dispute.Classsessionid.Value)
                .OrderBy(x => x.Schedulechangeid)
                .ToListAsync()
            : new List<ClassSessionScheduleChange>();
        var confirmerIds = scheduleChanges
            .SelectMany(x => new[] { x.Tutorconfirmedby, x.Learnerconfirmedby })
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Cast<string>()
            .Distinct()
            .ToList();
        var confirmerNames = await _context.Users.AsNoTracking()
            .Where(x => confirmerIds.Contains(x.Userid))
            .ToDictionaryAsync(x => x.Userid, x => x.Fullname ?? x.Username ?? x.Email);
        return new DisputeDetailResponse
        {
            DisputeId = dispute.Disputeid,
            BookingId = dispute.Bookingid,
            ClassSessionId = dispute.Classsessionid,
            DisputeType = dispute.Disputetype,
            Reason = dispute.Reason,
            Status = dispute.Status,
            Priority = dispute.Priority,
            PriorityReason = dispute.Priorityreason,
            Evidence = DeserializeJsonList(dispute.Evidence),
            CreatedAt = dispute.Createdat.HasValue ? dispute.Createdat.Value : (DateTime?)null,
            ResolvedAt = dispute.Resolvedat.HasValue ? dispute.Resolvedat.Value : (DateTime?)null,
            ResolutionNote = dispute.Resolutionnote,
            RefundAmount = dispute.Refundamount,
            RefundPercentage = dispute.Refundpercentage,
            TutorResponse = dispute.Tutorresponse,
            TutorRespondedAt = dispute.Tutorrespondedat,
            RespondentResponse = dispute.Respondentresponse,
            RespondentRespondedAt = dispute.Respondentrespondedat,
            NoShowConfirmedAt = dispute.Noshowconfirmedat,
            NoShowConfirmedBy = dispute.Noshowconfirmedby,
            AdditionalEvidence = dispute.DisputeEvidences?.Count > 0
                ? dispute.DisputeEvidences.Select(e => new DisputeEvidenceItemResponse
                {
                    DisputeEvidenceId = e.Disputeevidenceid,
                    FileUrl = e.Fileurl,
                    FileType = e.Filetype,
                    Description = e.Description,
                    CreatedAt = e.Createdat,
                    Source = string.IsNullOrWhiteSpace(e.Uploadedby)
                        ? "unknown"
                        : e.Uploadedby == dispute.ClassSession?.Tutorid ? "tutor" : "learner",
                    UploadedByName = e.UploadedbyNavigation?.Fullname
                        ?? e.UploadedbyNavigation?.Username
                        ?? e.UploadedbyNavigation?.Email
                }).OrderBy(e => e.CreatedAt).ToList()
                : null,
            CreatedBy = dispute.CreatedbyNavigation != null ? new DisputeUserResponse
            {
                UserId = dispute.Createdby,
                FullName = dispute.CreatedbyNavigation.Fullname,
                Email = dispute.CreatedbyNavigation.Email,
                Phone = dispute.CreatedbyNavigation.Phone,
                AvatarUrl = dispute.CreatedbyNavigation.Avatarurl
            } : null,
            ResolvedBy = dispute.ResolvedbyNavigation != null ? new DisputeUserResponse
            {
                UserId = dispute.Resolvedby,
                FullName = dispute.ResolvedbyNavigation.Fullname,
                Email = dispute.ResolvedbyNavigation.Email,
                AvatarUrl = dispute.ResolvedbyNavigation.Avatarurl
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
                ScheduleChanges = scheduleChanges.Select(x => new DisputeScheduleChangeAuditResponse
                {
                    ScheduleChangeId = x.Schedulechangeid,
                    Status = x.Status,
                    OriginalScheduledStart = x.Originalscheduledstart,
                    OriginalScheduledEnd = x.Originalscheduledend,
                    AdjustedScheduledStart = x.Adjustedscheduledstart,
                    AdjustedScheduledEnd = x.Adjustedscheduledend,
                    LearnerApproverRole = x.Learnerapproverrole,
                    TutorConfirmedByName = x.Tutorconfirmedby != null && confirmerNames.TryGetValue(x.Tutorconfirmedby, out var tutorName) ? tutorName : null,
                    TutorConfirmedAt = x.Tutorconfirmedat,
                    LearnerConfirmedByName = x.Learnerconfirmedby != null && confirmerNames.TryGetValue(x.Learnerconfirmedby, out var learnerName) ? learnerName : null,
                    LearnerConfirmedAt = x.Learnerconfirmedat,
                    RequestedAt = x.Requestedat,
                    ApprovedAt = x.Approvedat,
                    AppliedAt = x.Appliedat
                }).ToList(),
                RecordingStatus = recordingStatus,
                RecordingUrl = recordingStreamUrl
            } : null,
            Tutor = dispute.ClassSession?.Tutor?.Tutor != null ? new DisputeTutorResponse
            {
                TutorId = dispute.ClassSession.Tutorid,
                FullName = dispute.ClassSession.Tutor.Tutor.Fullname,
                Email = dispute.ClassSession.Tutor.Tutor.Email,
                Phone = dispute.ClassSession.Tutor.Tutor.Phone,
                AvatarUrl = dispute.ClassSession.Tutor.Tutor.Avatarurl,
                WarningCount = warningCount,
                AverageRating = dispute.ClassSession.Tutor.Averagerating.HasValue ? (decimal?)dispute.ClassSession.Tutor.Averagerating.Value : null
            } : null
        };
    }

    /// <summary>
    /// Runs AI (Groq) priority classification for a dispute and persists the result. actorId scopes the
    /// returned DisputeDetailResponse's recording stream token; pass "system" for the background
    /// (Hangfire) trigger, where the response is discarded anyway.
    /// </summary>
    public async Task<DisputeDetailResponse?> ClassifyDisputePriorityAsync(int disputeId, string actorId, bool swallowClassificationFailure = false)
    {
        var dispute = await _disputeRepo.FindWithClassSessionAsync(disputeId);
        if (dispute == null)
        {
            _logger.LogWarning("ClassifyDisputePriorityAsync: dispute {DisputeId} not found", disputeId);
            return null;
        }

        _logger.LogInformation("ClassifyDisputePriorityAsync started for dispute {DisputeId} (type={DisputeType})", disputeId, dispute.Disputetype);

        try
        {
            var classification = await _classificationService.ClassifyAsync(dispute.Disputetype ?? "", dispute.Reason ?? "");
            dispute.Priority = classification.Priority;
            dispute.Priorityreason = classification.Reason;
            await _disputeRepo.SaveChangesAsync();
            _logger.LogInformation("Dispute {DisputeId} classified as priority {Priority}", disputeId, classification.Priority);
        }
        catch (Exception ex) when (swallowClassificationFailure)
        {
            // Chỉ nuốt lỗi cho nhánh Hangfire (background): không được để lỗi gọi AI (key sai/thiếu,
            // Groq lỗi, response không hợp lệ) làm job fail — Hangfire fail vĩnh viễn sau vài lần retry
            // và không có gì tự enqueue lại, khiến Priority treo NULL mãi mãi ("CHƯA PHÂN LOẠI" không
            // bao giờ hết). Nhánh admin phân loại tay (swallowClassificationFailure=false) vẫn phải throw
            // để controller trả lỗi rõ ràng thay vì báo thành công giả.
            _logger.LogError(ex, "Không thể phân loại ưu tiên cho tranh chấp {DisputeId} — giữ nguyên chưa phân loại.", disputeId);
        }

        return await GetDisputeDetailAsync(disputeId, actorId);
    }

    public async Task<DisputeRecordingResponse> GetDisputeRecordingAsync(int disputeId, string actorId)
    {
        var dispute = await _disputeRepo.FindWithClassSessionAsync(disputeId)
            ?? throw new ArgumentException("Không tìm thấy tranh chấp");

        var cs = dispute.ClassSession;
        var (status, url) = RecordingStatusResolver.Resolve(cs?.Recordingurl, cs?.Recordings3key, cs?.Recordingsid, cs?.Checkouttime.HasValue ?? false);
        var streamUrl = BuildRecordingStreamUrl(dispute.Classsessionid, actorId, url);

        return new DisputeRecordingResponse
        {
            DisputeId = dispute.Disputeid,
            ClassSessionId = dispute.Classsessionid,
            Status = status,
            RecordingUrl = streamUrl,
            Available = streamUrl != null
        };
    }

    /// <summary>
    /// Phát hành token ngắn hạn + dựng link stream proxy cho actorId xem bản ghi của classSessionId.
    /// Trả null nếu chưa có bản ghi (rawUrl null) — không có gì để phát token cho.
    /// </summary>
    private string? BuildRecordingStreamUrl(int? classSessionId, string actorId, string? rawUrl)
    {
        if (rawUrl == null || classSessionId == null) return null;
        var token = _recordingAccessTokenService.Issue(classSessionId.Value, actorId, RecordingTokenLifetime);
        return $"/api/class-sessions/{classSessionId.Value}/recording/stream?token={Uri.EscapeDataString(token)}";
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
            SenderName = m.Sender?.Fullname,
            SenderAvatarUrl = m.Sender?.Avatarurl,
            Content = m.Content ?? string.Empty,
            MessageType = m.Messagetype ?? ChatMessageType.Text,
            CreatedAt = m.Createdat.HasValue ? m.Createdat.Value : (DateTime?)null
        }).ToList();
    }

    /// <summary>Tutor gets 48h from dispute creation to submit a rebuttal before admin can start investigating.</summary>
    public const int TutorResponseGraceHours = 48;

    public async Task<DisputeDetailResponse> InvestigateDisputeAsync(int disputeId, string adminId, bool forceEarly = false)
    {
        var dispute = await _disputeRepo.FindWithClassSessionAsync(disputeId)
            ?? throw new ArgumentException("Không tìm thấy tranh chấp");

        if (dispute.Status != DisputeStatus.Pending)
            throw new InvalidOperationException("Tranh chấp chưa ở trạng thái chờ xử lý");

        if (!forceEarly)
        {
            var elapsedHours = (MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow - (dispute.Createdat ?? MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow)).TotalHours;
            if (elapsedHours < TutorResponseGraceHours)
                throw new InvalidOperationException(
                    $"Cần chờ đủ {TutorResponseGraceHours}h kể từ khi tạo tranh chấp để gia sư có thời gian phản hồi trước khi điều tra. Dùng forceEarly nếu cần bắt đầu sớm.");
        }

        dispute.Status = DisputeStatus.Investigating;
        await _disputeRepo.SaveChangesAsync();

        _logger.LogInformation("Actor {ActorId} started investigating dispute {DisputeId} (forceEarly={ForceEarly})", adminId, disputeId, forceEarly);

        return (await GetDisputeDetailAsync(disputeId, adminId))!;
    }

    public async Task<DisputeDetailResponse> ConfirmTutorNoShowAsync(int disputeId, string adminId)
    {
        var snapshot = await _context.Disputes
            .AsNoTracking()
            .Where(d => d.Disputeid == disputeId)
            .Select(d => new { d.Bookingid, d.Createdby })
            .FirstOrDefaultAsync()
            ?? throw new ArgumentException("Không tìm thấy tranh chấp");

        var newlyConfirmed = false;
        int classSessionId;
        string? tutorId;

        await using (var tx = await _context.Database.BeginTransactionAsync())
        {
            try
            {
                // Keep a single lock order across admin confirmation, admin verdict and payer-side remedy:
                // booking -> dispute -> class session -> wallets.
                if (snapshot.Bookingid.HasValue)
                {
                    _ = await _context.Bookings
                        .FromSqlRaw(SqlQueries.LockBookingById, snapshot.Bookingid.Value)
                        .AsNoTracking()
                        .SingleOrDefaultAsync()
                        ?? throw new InvalidOperationException("Không tìm thấy booking của tranh chấp");
                }

                var dispute = await _context.Disputes
                    .FromSqlRaw(SqlQueries.LockDisputeById, disputeId)
                    .SingleOrDefaultAsync()
                    ?? throw new ArgumentException("Không tìm thấy tranh chấp");

                if (dispute.Status is DisputeStatus.Resolved or DisputeStatus.Closed)
                    throw new InvalidOperationException("Tranh chấp này đã được giải quyết rồi");
                if (dispute.Disputetype != DisputeTypes.NoShow)
                    throw new InvalidOperationException("Chỉ tranh chấp vắng mặt mới có thể xác nhận theo luồng này");
                if (!dispute.Classsessionid.HasValue)
                    throw new InvalidOperationException("Tranh chấp không gắn với buổi học");

                var classSession = await _context.ClassSessions
                    .FromSqlRaw(SqlQueries.LockClassSessionById, dispute.Classsessionid.Value)
                    .SingleOrDefaultAsync()
                    ?? throw new InvalidOperationException("Không tìm thấy buổi học của tranh chấp");

                classSessionId = classSession.Classsessionid;
                tutorId = classSession.Tutorid;

                if (dispute.Status != DisputeStatus.ConfirmedNoShow)
                {
                    if (dispute.Status is not (DisputeStatus.Pending or DisputeStatus.Investigating))
                        throw new InvalidOperationException("Tranh chấp không ở trạng thái có thể xác nhận vắng mặt");
                    if (classSession.Status != NoShow || classSession.Issettled == true)
                        throw new InvalidOperationException("Buổi học không còn chờ xác nhận vắng mặt");

                    dispute.Status = DisputeStatus.ConfirmedNoShow;
                    dispute.Noshowconfirmedat = TimeZoneHelper.UtcNow;
                    dispute.Noshowconfirmedby = adminId;
                    newlyConfirmed = true;
                    await _context.SaveChangesAsync();
                }

                await tx.CommitAsync();
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }

        if (newlyConfirmed)
        {
            _logger.LogInformation(
                "Admin {AdminId} confirmed tutor no-show for dispute {DisputeId}, class session {ClassSessionId}",
                adminId, disputeId, classSessionId);

            try
            {
                var notifications = new List<NotificationRequest>();
                if (!string.IsNullOrWhiteSpace(snapshot.Createdby))
                    notifications.Add(new NotificationRequest
                    {
                        Userid = snapshot.Createdby,
                        Title = "Báo cáo vắng mặt đã được xác nhận",
                        Message = $"Admin đã xác nhận gia sư vắng mặt ở buổi học #{classSessionId}. Bạn có thể chọn phương án xử lý.",
                        Type = NotificationType.DisputeResolved,
                        Referenceid = classSessionId.ToString()
                    });
                if (!string.IsNullOrWhiteSpace(tutorId))
                    notifications.Add(new NotificationRequest
                    {
                        Userid = tutorId,
                        Title = "Xác nhận vắng mặt",
                        Message = $"Admin đã xác nhận báo cáo vắng mặt cho buổi học #{classSessionId}.",
                        Type = NotificationType.DisputeResolved,
                        Referenceid = classSessionId.ToString()
                    });
                if (notifications.Count > 0)
                    await _notificationService.CreateNotificationsAsync(notifications);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send no-show confirmation notifications for dispute {DisputeId}", disputeId);
            }
        }

        return (await GetDisputeDetailAsync(disputeId, adminId))!;
    }
    public async Task<DisputeDetailResponse> CloseDisputeAsync(int disputeId, string adminId, CloseDisputeRequest request)
    {
        if (!CloseDisputeOutcomes.All.Contains(request.ClassSessionOutcome))
            throw new ArgumentException("Trạng thái buổi học không hợp lệ");

        var snapshot = await _context.Disputes
            .AsNoTracking()
            .Where(d => d.Disputeid == disputeId)
            .Select(d => new { d.Bookingid })
            .FirstOrDefaultAsync()
            ?? throw new ArgumentException("Không tìm thấy tranh chấp");

        string? createdBy;
        int? classSessionId;

        await using (var tx = await _context.Database.BeginTransactionAsync())
        {
            try
            {
                // Cùng thứ tự khoá với ResolveDisputeAsync để không kẹt chéo với remedy no-show phía người dùng.
                Booking? booking = null;
                if (snapshot.Bookingid.HasValue)
                {
                    booking = await _context.Bookings
                        .FromSqlRaw(SqlQueries.LockBookingById, snapshot.Bookingid.Value)
                        .SingleOrDefaultAsync()
                        ?? throw new InvalidOperationException("Không tìm thấy booking của tranh chấp");
                }

                var dispute = await _context.Disputes
                    .FromSqlRaw(SqlQueries.LockDisputeById, disputeId)
                    .SingleOrDefaultAsync()
                    ?? throw new ArgumentException("Không tìm thấy tranh chấp");

                if (dispute.Status is DisputeStatus.Resolved or DisputeStatus.Closed)
                    throw new InvalidOperationException("Tranh chấp này đã được xử lý rồi");

                // Booking đã bị huỷ theo remedy "đổi gia sư" thì tiền các buổi còn lại đã hoàn về ví phụ
                // huynh và mọi buổi tương lai đã bị huỷ — hoà giải không thể dựng lại được, chặn từ đây
                // thay vì để admin đóng xong rồi phát hiện booking vẫn không học tiếp được.
                if (booking?.Status == BookingStatus.CancelledNoshow)
                    throw new InvalidOperationException(
                        "Booking đã bị huỷ và hoàn tiền theo phương án đổi gia sư, không thể khôi phục bằng hoà giải.");

                ClassSession? classSession = null;
                if (dispute.Classsessionid.HasValue)
                {
                    classSession = await _context.ClassSessions
                        .FromSqlRaw(SqlQueries.LockClassSessionById, dispute.Classsessionid.Value)
                        .Include(l => l.Booking)
                        .SingleOrDefaultAsync()
                        ?? throw new InvalidOperationException("Không tìm thấy buổi học của tranh chấp");
                }

                var now = TimeZoneHelper.UtcNow;

                if (classSession != null)
                {
                    if (request.ClassSessionOutcome == CloseDisputeOutcomes.Completed)
                    {
                        // SettleDisputedClassSessionAsync cố ý bỏ qua kiểm tra trạng thái — cần thế vì buổi
                        // đang ở "disputed", còn SettleClassSessionAsync chỉ nhận pending_confirmation/completed.
                        await _settlementService.SettleDisputedClassSessionAsync(classSession.Classsessionid, adminId);
                        classSession.Status = Completed;
                    }
                    else
                    {
                        if (classSession.Issettled == true)
                            throw new InvalidOperationException(
                                "Buổi học này đã được quyết toán, không thể đưa về trạng thái học lại.");

                        // Học lại thì lần dạy hỏng trước đó không được để lại dấu vết, nếu không buổi mới
                        // sẽ mang sẵn giờ check-in/điểm danh cũ và báo cáo cũ của lần trước.
                        classSession.Status = Scheduled;
                        classSession.Checkintime = null;
                        classSession.Checkouttime = null;
                        classSession.Realstart = null;
                        classSession.Realend = null;
                        classSession.Istutorpresent = null;
                        classSession.Isstudentpresent = null;
                        classSession.Attendancenote = null;
                        classSession.Noshowaction = null;
                        classSession.Submittedat = null;
                        classSession.Confirmdeadline = null;
                    }
                }

                dispute.Status = DisputeStatus.Closed;
                dispute.Resolvedat = now;
                dispute.Resolvedby = adminId;
                dispute.Resolutionnote = request.Note;
                // Không bên nào được hoàn tiền — hoà giải không phải phán quyết, để 0 cho khớp báo cáo tài chính.
                dispute.Refundpercentage = 0;

                createdBy = dispute.Createdby;
                classSessionId = dispute.Classsessionid;

                await _context.SaveChangesAsync();
                await tx.CommitAsync();
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }

        _logger.LogInformation("Actor {ActorId} closed dispute {DisputeId} by mutual agreement, classSession outcome {Outcome}",
            adminId, disputeId, request.ClassSessionOutcome);

        try
        {
            var outcomeText = request.ClassSessionOutcome == CloseDisputeOutcomes.Completed
                ? "Buổi học vẫn được tính là đã hoàn thành."
                : "Buổi học sẽ được sắp xếp học lại.";

            if (!string.IsNullOrWhiteSpace(createdBy))
            {
                await _notificationService.CreateNotificationAsync(new NotificationRequest
                {
                    Userid = createdBy,
                    Title = "Phản ánh đã được đóng",
                    Type = NotificationType.DisputeResolved,
                    Message = $"Phản ánh #{disputeId} đã được đóng do hai bên đã thống nhất với nhau. {outcomeText} Ghi chú: {request.Note}",
                    Referenceid = classSessionId?.ToString()
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send dispute close notification for dispute {DisputeId}", disputeId);
        }

        return (await GetDisputeDetailAsync(disputeId, adminId))!;
    }

    public async Task<DisputeDetailResponse> ResolveDisputeAsync(int disputeId, string adminId, ResolveDisputeRequest request)
    {
        if (!ResolutionTypes.All.Contains(request.ResolutionType))
            throw new ArgumentException("Loại kết quả xử lý không hợp lệ");

        if (request.ResolutionType == ResolutionTypes.Custom && !request.CustomRefundPercentage.HasValue)
            throw new ArgumentException("Cần nhập phần trăm hoàn tiền tùy chỉnh");

        var snapshot = await _context.Disputes
            .AsNoTracking()
            .Where(d => d.Disputeid == disputeId)
            .Select(d => new { d.Bookingid, d.Classsessionid })
            .FirstOrDefaultAsync()
            ?? throw new ArgumentException("Không tìm thấy tranh chấp");

        string? createdBy;
        string? tutorId;
        decimal courseCancelRefund = 0;

        await using (var tx = await _context.Database.BeginTransactionAsync())
        {
            try
            {
                // Serialize against payer-side no-show remedies using the same lock order.
                if (snapshot.Bookingid.HasValue)
                {
                    _ = await _context.Bookings
                        .FromSqlRaw(SqlQueries.LockBookingById, snapshot.Bookingid.Value)
                        .AsNoTracking()
                        .SingleOrDefaultAsync()
                        ?? throw new InvalidOperationException("Không tìm thấy booking của tranh chấp");
                }

                var dispute = await _context.Disputes
                    .FromSqlRaw(SqlQueries.LockDisputeById, disputeId)
                    .SingleOrDefaultAsync()
                    ?? throw new ArgumentException("Không tìm thấy tranh chấp");

                if (dispute.Status is DisputeStatus.Resolved or DisputeStatus.Closed)
                    throw new InvalidOperationException("Tranh chấp này đã được giải quyết rồi");

                ClassSession? classSession = null;
                if (dispute.Classsessionid.HasValue)
                {
                    classSession = await _context.ClassSessions
                        .FromSqlRaw(SqlQueries.LockClassSessionById, dispute.Classsessionid.Value)
                        .Include(l => l.Booking)
                            .ThenInclude(b => b!.Student)
                        .SingleOrDefaultAsync()
                        ?? throw new InvalidOperationException("Không tìm thấy buổi học của tranh chấp");
                }

                var now = TimeZoneHelper.UtcNow;
                var refundPercentage = request.ResolutionType switch
                {
                    ResolutionTypes.Release => 0,
                    ResolutionTypes.Refund50 => 50,
                    ResolutionTypes.Refund100 => 100,
                    ResolutionTypes.Custom => request.CustomRefundPercentage!.Value,
                    // Buổi đang bị khiếu nại được settle như đã dạy đủ (gia sư giữ tiền buổi đó) —
                    // phần hoàn tiền của lựa chọn này nằm ở CancelRemainingSessionsAsync bên dưới,
                    // áp dụng cho các buổi CHƯA diễn ra của cả khóa học, không phải buổi này.
                    ResolutionTypes.CancelCourse => 0,
                    _ => 0
                };

                if (classSession != null)
                {
                    if (refundPercentage > 0)
                        await _settlementService.ProcessRefundAsync(classSession.Classsessionid, refundPercentage, adminId);
                    else
                        await _settlementService.SettleDisputedClassSessionAsync(classSession.Classsessionid, adminId);

                    if (request.ResolutionType == ResolutionTypes.CancelCourse && dispute.Bookingid.HasValue)
                        courseCancelRefund = await _settlementService.CancelRemainingSessionsAsync(
                            dispute.Bookingid.Value,
                            adminId,
                            BookingStatus.CancelledByDispute,
                            $"Hủy khóa học theo giải quyết tranh chấp #{disputeId}: {request.ResolutionNote}",
                            CancellationToken.None);
                }

                dispute.Status = DisputeStatus.Resolved;
                dispute.Resolvedat = now;
                dispute.Resolvedby = adminId;
                dispute.Resolutionnote = request.ResolutionNote;
                dispute.Refundpercentage = refundPercentage;

                tutorId = classSession?.Tutorid;
                createdBy = dispute.Createdby;

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

                if (classSession != null)
                    classSession.Status = refundPercentage == 100 ? Cancelled : Completed;

                await _context.SaveChangesAsync();
                await tx.CommitAsync();
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }

        _logger.LogInformation("Actor {ActorId} resolved dispute {DisputeId} with {Resolution}",
            adminId, disputeId, request.ResolutionType);

        try
        {
            var notifications = new List<NotificationRequest>();
            if (!string.IsNullOrWhiteSpace(createdBy))
            {
                var courseCancelSuffix = request.ResolutionType == ResolutionTypes.CancelCourse
                    ? $" Khóa học đã bị hủy, bạn đã nhận hoàn {courseCancelRefund:N0}đ cho các buổi chưa học."
                    : "";
                notifications.Add(new NotificationRequest
                {
                    Userid = createdBy,
                    Title = "Tranh chấp đã được giải quyết",
                    Message = $"Tranh chấp #{disputeId} đã được giải quyết. Kết quả: {request.ResolutionType}. Ghi chú: {request.ResolutionNote}{courseCancelSuffix}",
                    Type = NotificationType.DisputeResolved,
                    Referenceid = snapshot.Classsessionid?.ToString()
                });
            }

            if (tutorId != null)
                notifications.Add(new NotificationRequest { Userid = tutorId, Title = "Thông báo giải quyết tranh chấp",
                    Message = $"Tranh chấp #{disputeId} liên quan đến buổi học của bạn đã được giải quyết. Kết quả: {request.ResolutionType}.",
                    Type = NotificationType.DisputeResolved,
                    Referenceid = snapshot.Classsessionid?.ToString() });

            await _notificationService.CreateNotificationsAsync(notifications);

            return (await GetDisputeDetailAsync(disputeId, adminId))!;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send dispute resolution notifications for dispute {DisputeId}", disputeId);
        }

        return (await GetDisputeDetailAsync(disputeId, adminId))!;
    }
    public async Task<RefundPreviewResponse> GetRefundPreviewAsync(int disputeId, int percentage)
    {
        var dispute = await _context.Disputes.AsNoTracking().FirstOrDefaultAsync(d => d.Disputeid == disputeId)
            ?? throw new ArgumentException("Không tìm thấy tranh chấp");

        if (!dispute.Classsessionid.HasValue)
            throw new ArgumentException("Tranh chấp này không gắn với buổi học nào để tính hoàn tiền");

        return await _settlementService.PreviewRefundAsync(dispute.Classsessionid.Value, percentage);
    }

    /// <summary>
    /// Xem trước số buổi/số tiền sẽ hủy+hoàn nếu resolve dispute bằng "Hủy khóa học & hoàn tiền"
    /// (ResolutionTypes.CancelCourse) — không side effect.
    /// </summary>
    public async Task<CourseCancelPreviewResponse> GetCancelCoursePreviewAsync(int disputeId)
    {
        var dispute = await _context.Disputes.AsNoTracking().FirstOrDefaultAsync(d => d.Disputeid == disputeId)
            ?? throw new ArgumentException("Không tìm thấy tranh chấp");

        if (!dispute.Bookingid.HasValue)
            throw new ArgumentException("Tranh chấp này không gắn với booking nào để tính hủy khóa học");

        return await _settlementService.PreviewCancelRemainingSessionsAsync(dispute.Bookingid.Value, dispute.Classsessionid);
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

    // ── Parent/Student-facing ────────────────────────────────────────────────

    public async Task<DisputeDetailResponse?> GetDisputeByClassSessionForUserAsync(int classSessionId, string userId, string role)
    {
        var studentIds = role == UserRole.Parent
            ? await _context.Studentprofiles.Where(s => s.Parentid == userId).Select(s => s.Studentid).ToListAsync()
            : await _context.Studentprofiles.Where(s => s.Studentid == userId || s.Linkeduserid == userId).Select(s => s.Studentid).ToListAsync();

        var disputeId = await _context.Disputes
            .Where(d => d.Classsessionid == classSessionId && studentIds.Contains(d.ClassSession!.Studentid!))
            .Select(d => (int?)d.Disputeid)
            .FirstOrDefaultAsync();

        return disputeId.HasValue ? await GetDisputeDetailAsync(disputeId.Value, userId) : null;
    }

    // ── Tutor-facing (rebuttal channel) ─────────────────────────────────────

    public async Task<DisputeDetailResponse?> GetTutorDisputeByClassSessionAsync(int classSessionId, string tutorId)
    {
        var disputeId = await _context.Disputes
            .Where(d => d.Classsessionid == classSessionId && d.ClassSession!.Tutorid == tutorId)
            .Select(d => (int?)d.Disputeid)
            .FirstOrDefaultAsync();

        return disputeId.HasValue ? await GetDisputeDetailAsync(disputeId.Value, tutorId) : null;
    }

    public async Task<PagedList<DisputeListResponse>> GetTutorDisputesAsync(
        string tutorId,
        PortalDisputeQueryRequest query)
    {
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 50);

        var disputesQuery = _context.Disputes
            .AsNoTracking()
            .Where(d => d.ClassSession!.Tutorid == tutorId);

        disputesQuery = disputesQuery.ApplyPortalFilters(query);
        var orderedDisputes = disputesQuery.OrderForDisputeList(query.SortDirection);

        var totalCount = await disputesQuery.CountAsync();
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
        page = Math.Min(page, totalPages);

        var disputes = await orderedDisputes
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(d => new DisputeListResponse
            {
                DisputeId = d.Disputeid,
                ClassSessionId = d.Classsessionid,
                BookingId = d.Bookingid,
                DisputeType = d.Disputetype,
                Status = d.Status,
                Reason = d.Reason,
                Priority = d.Priority,
                PriorityReason = d.Priorityreason,
                ClassSessionPrice = d.ClassSession!.Lessonprice,
                CreatedAt = d.Createdat
            })
            .ToListAsync();

        return new PagedList<DisputeListResponse>(disputes, totalCount, page, pageSize);
    }

    /// <summary>
    /// Tutor submits a written rebuttal to a dispute raised against them. Text-only — evidence
    /// files go through <see cref="UploadTutorDisputeEvidenceAsync"/> and attach independently,
    /// so a tutor can add evidence without having written the rebuttal yet (and vice versa).
    /// </summary>
    public async Task<DisputeDetailResponse> SubmitTutorResponseAsync(int classSessionId, string tutorId, string response)
    {
        var dispute = await _context.Disputes
            .FirstOrDefaultAsync(d => d.Classsessionid == classSessionId && d.ClassSession!.Tutorid == tutorId)
            ?? throw new ArgumentException("Không tìm thấy tranh chấp cho buổi học này");

        // Gia sư giờ cũng tự tạo dispute được (xem CreateTutorDisputeAsync) — chặn việc gia sư
        // "phản hồi" chính dispute do họ mở; bên phải phản hồi khi đó là phụ huynh/học sinh,
        // qua SubmitRespondentResponseAsync.
        if (dispute.Createdby == tutorId)
            throw new InvalidOperationException("Bạn là người tạo tranh chấp này, không thể tự phản hồi.");

        if (dispute.Status != DisputeStatus.Pending)
            throw new InvalidOperationException("Tranh chấp đã bước vào giai đoạn điều tra hoặc đã được giải quyết, không thể phản hồi thêm vào hồ sơ. Dùng kênh chat để trao đổi thêm với admin.");

        dispute.Tutorresponse = response;
        dispute.Tutorrespondedat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;
        await _context.SaveChangesAsync();

        _logger.LogInformation("Tutor {TutorId} submitted a response to dispute {DisputeId}", tutorId, dispute.Disputeid);

        if (!string.IsNullOrEmpty(dispute.Createdby))
        {
            try
            {
                await _notificationService.CreateNotificationAsync(new NotificationRequest
                {
                    Userid = dispute.Createdby,
                    Title = "Gia sư đã phản hồi khiếu nại",
                    Message = $"Gia sư đã gửi phản hồi cho khiếu nại #{dispute.Disputeid}. Admin sẽ xem xét và xử lý.",
                    Type = NotificationType.DisputeResponded,
                    Referenceid = dispute.Classsessionid?.ToString()
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to notify dispute creator {CreatedBy} of tutor response for dispute {DisputeId}",
                    dispute.Createdby, dispute.Disputeid);
            }
        }

        return (await GetDisputeDetailAsync(dispute.Disputeid, tutorId))!;
    }

    public async Task<string> UploadTutorDisputeEvidenceAsync(int classSessionId, string tutorId, IFormFile file)
    {
        // The HTTP endpoint rejects an empty upload before reaching here, but the service is also
        // called directly; without this it fails as a NullReferenceException deep inside the
        // storage call instead of saying what is wrong.
        if (file == null || file.Length == 0)
            throw new ArgumentException("Tệp bằng chứng là bắt buộc.");

        var dispute = await _context.Disputes
            .FirstOrDefaultAsync(d => d.Classsessionid == classSessionId && d.ClassSession!.Tutorid == tutorId)
            ?? throw new ArgumentException("Không tìm thấy tranh chấp cho buổi học này");

        if (dispute.Createdby == tutorId)
            throw new InvalidOperationException("Bạn là người tạo tranh chấp này, không thể tự nộp bằng chứng phản hồi.");

        if (dispute.Status != DisputeStatus.Pending)
            throw new InvalidOperationException("Tranh chấp đã bước vào giai đoạn điều tra hoặc đã được giải quyết, không thể nộp thêm bằng chứng vào hồ sơ. Dùng kênh chat để trao đổi thêm với admin.");

        await _storageService.EnsureBucketExistsAsync(StorageBucket.ClassSessionAttachments);
        var folderPath = $"dispute-evidence-{classSessionId}";
        var fileUrl = await _storageService.UploadFileAsync(StorageBucket.ClassSessionAttachments, folderPath, file);

        _context.DisputeEvidences.Add(new DisputeEvidence
        {
            Disputeid = dispute.Disputeid,
            Uploadedby = tutorId,
            Fileurl = fileUrl,
            Filetype = file.ContentType,
            Createdat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow
        });
        await _context.SaveChangesAsync();

        return fileUrl;
    }

    /// <summary>
    /// Gia sư tự mở 1 tranh chấp mới cho buổi học của mình (vd học sinh/phụ huynh không tới,
    /// vấn đề thanh toán...) — đối xứng với <see cref="ParentService.CreateDisputeAsync"/>,
    /// chỉ khác ở bước xác định quyền sở hữu buổi học (theo Tutorid thay vì studentIds).
    /// Không xây pipeline riêng cho no_show — dùng chung luồng dispute thường, admin xử lý qua
    /// <see cref="ResolveDisputeAsync"/> như mọi loại tranh chấp khác.
    /// </summary>
    public async Task<DisputeDetailResponse> CreateTutorDisputeAsync(int classSessionId, string tutorId, CreateDisputeRequest request)
    {
        if (!DisputeTypes.All.Contains(request.DisputeType))
            throw new ArgumentException("Loại tranh chấp không hợp lệ");

        var snapshot = await _context.ClassSessions
            .AsNoTracking()
            .Where(l => l.Classsessionid == classSessionId && l.Tutorid == tutorId)
            .Select(l => new
            {
                l.Bookingid,
                l.Status,
                BookingStatus = l.Booking != null ? l.Booking.Status : null
            })
            .FirstOrDefaultAsync()
            ?? throw new ArgumentException("Không tìm thấy buổi học hoặc bạn không có quyền truy cập");

        if (!snapshot.Bookingid.HasValue)
            throw new InvalidOperationException("Buổi học không có booking hợp lệ");
        if (DisputeSettlementPolicy.IsTerminalBooking(snapshot.BookingStatus))
            throw new InvalidOperationException("Booking đã kết thúc, không thể tạo tranh chấp mới");
        if (!DisputeSettlementPolicy.IsEligibleClassSession(snapshot.Status))
            throw new InvalidOperationException("Chỉ buổi học đã diễn ra mới có thể tạo tranh chấp");

        var uploadedEvidence = new List<string>();
        var evidenceFolder = $"dispute-evidence-{classSessionId}";

        if (request.Files?.Count > 0)
        {
            await _storageService.EnsureBucketExistsAsync(StorageBucket.ClassSessionAttachments);
            foreach (var file in request.Files.Where(f => f is { Length: > 0 }))
            {
                var fileUrl = await _storageService.UploadFileAsync(
                    StorageBucket.ClassSessionAttachments,
                    evidenceFolder,
                    file);
                uploadedEvidence.Add(fileUrl);
            }
        }

        if (request.Evidence?.Count > 0)
            uploadedEvidence.AddRange(request.Evidence.Where(url => !string.IsNullOrWhiteSpace(url)));

        Dispute dispute;
        Booking booking;

        // Serialize with settlement/admin resolution using the shared lock order.
        await using (var tx = await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable))
        {
            try
            {
                booking = await _context.Bookings
                    .FromSqlRaw(SqlQueries.LockBookingById, snapshot.Bookingid.Value)
                    .SingleOrDefaultAsync()
                    ?? throw new ArgumentException("Không tìm thấy booking của buổi học");

                var classSession = await _context.ClassSessions
                    .FromSqlRaw(SqlQueries.LockClassSessionById, classSessionId)
                    .SingleOrDefaultAsync()
                    ?? throw new ArgumentException("Không tìm thấy buổi học");

                if (classSession.Tutorid != tutorId)
                    throw new ArgumentException("Bạn không có quyền truy cập buổi học này");
                if (DisputeSettlementPolicy.IsTerminalBooking(booking.Status))
                    throw new InvalidOperationException("Booking đã kết thúc, không thể tạo tranh chấp mới");
                if (!DisputeSettlementPolicy.IsEligibleClassSession(classSession.Status))
                    throw new InvalidOperationException("Chỉ buổi học đã diễn ra mới có thể tạo tranh chấp");
                if (await _context.Disputes.AnyAsync(d => d.Classsessionid == classSessionId))
                    throw new InvalidOperationException("Buổi học này đã có tranh chấp rồi");

                if (classSession.Issettled == true)
                {
                    // Settlement already reduced this counter once. Reopen exactly one unit;
                    // no wallet balance changes until admin chooses Release/Refund.
                    classSession.Issettled = false;
                    booking.Sessionsremaining = DisputeSettlementPolicy.SessionsRemainingAfterOpeningDispute(
                        booking.Sessionsremaining,
                        wasSettled: true);
                    booking.Updatedat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;
                }

                dispute = new Dispute
                {
                    Classsessionid = classSessionId,
                    Bookingid = classSession.Bookingid,
                    Createdby = tutorId,
                    Disputetype = request.DisputeType,
                    Reason = request.Reason,
                    Status = DisputeStatus.Pending,
                    Evidence = uploadedEvidence.Count > 0
                        ? JsonSerializer.Serialize(uploadedEvidence.Distinct())
                        : null,
                    Createdat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow
                };

                _context.Disputes.Add(dispute);
                classSession.Status = Disputed;
                await _context.SaveChangesAsync();
                await tx.CommitAsync();
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }

        try
        {
            _backgroundJobClient.Enqueue<IDisputeService>(
                s => s.ClassifyDisputePriorityAsync(dispute.Disputeid, "system", true));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enqueue priority classification job for dispute {DisputeId}", dispute.Disputeid);
        }

        _logger.LogInformation(
            "Tutor {TutorId} created dispute {DisputeId} for classSession {ClassSessionId}",
            tutorId, dispute.Disputeid, classSessionId);

        try
        {
            var reviewerIds = await PermissionRecipients.ResolveAsync(_context, Permissions.DisputeView);
            if (reviewerIds.Count > 0)
            {
                await _notificationService.CreateNotificationsAsync(reviewerIds.Select(reviewerId => new NotificationRequest
                {
                    Userid = reviewerId,
                    Title = "Tranh chấp mới",
                    Message = $"Gia sư đã tạo tranh chấp cho buổi học #{classSessionId}. Lý do: {request.Reason}",
                    Type = NotificationType.DisputeNew,
                    Referenceid = dispute.Disputeid.ToString()
                }));
            }

            // Bên phải phản hồi: phụ huynh nếu có, không thì học sinh tự quản lý (Linkeduserid),
            // fallback cuối cùng Studentid — cùng quy tắc BookingPayerResolver nhưng truy vấn
            // trực tiếp vì entity Booking đã lock/detach xong, không có Student navigation kèm.
            var learnerInfo = await _context.Bookings.AsNoTracking()
                .Where(b => b.Bookingid == dispute.Bookingid)
                .Select(b => new
                {
                    b.Parentid,
                    b.Studentid,
                    StudentLinkedUserId = b.Student != null ? b.Student.Linkeduserid : null
                })
                .FirstOrDefaultAsync();
            var counterpartId = !string.IsNullOrWhiteSpace(learnerInfo?.Parentid)
                ? learnerInfo.Parentid
                : !string.IsNullOrWhiteSpace(learnerInfo?.StudentLinkedUserId)
                    ? learnerInfo.StudentLinkedUserId
                    : learnerInfo?.Studentid;

            if (!string.IsNullOrWhiteSpace(counterpartId))
            {
                await _notificationService.CreateNotificationAsync(new NotificationRequest
                {
                    Userid = counterpartId,
                    Title = "Có khiếu nại về buổi học của bạn",
                    Message = $"Gia sư đã tạo khiếu nại cho buổi học #{classSessionId}. Bạn có thể xem chi tiết và gửi phản hồi.",
                    Type = NotificationType.DisputeReceived,
                    Referenceid = classSessionId.ToString()
                });
            }
        }
        catch (Exception notificationError)
        {
            _logger.LogWarning(notificationError, "Dispute {DisputeId} was created but one or more notifications failed", dispute.Disputeid);
        }

        return (await GetDisputeDetailAsync(dispute.Disputeid, tutorId))!;
    }

    /// <summary>
    /// Phụ huynh/học sinh phản hồi 1 tranh chấp do GIA SƯ tạo cho buổi học của họ — đối xứng
    /// với <see cref="SubmitTutorResponseAsync"/>. Ghi vào <see cref="Dispute.Respondentresponse"/>
    /// (không phải Tutorresponse — cột đó chỉ đúng nghĩa khi phụ huynh/học sinh là người tạo).
    /// </summary>
    public async Task<DisputeDetailResponse> SubmitRespondentResponseAsync(int classSessionId, string userId, string role, string response)
    {
        var studentIds = role == UserRole.Parent
            ? await _context.Studentprofiles.Where(s => s.Parentid == userId).Select(s => s.Studentid).ToListAsync()
            : await _context.Studentprofiles.Where(s => s.Studentid == userId || s.Linkeduserid == userId).Select(s => s.Studentid).ToListAsync();

        if (role == UserRole.Student)
        {
            var studentProfile = await _context.Studentprofiles
                .FirstOrDefaultAsync(s => s.Studentid == userId || s.Linkeduserid == userId);
            if (studentProfile?.Parentid != null)
                throw new InvalidOperationException("Tài khoản học sinh do phụ huynh quản lý không thể tự phản hồi tranh chấp");
        }

        var dispute = await _context.Disputes
            .Include(d => d.ClassSession)
            .FirstOrDefaultAsync(d => d.Classsessionid == classSessionId && studentIds.Contains(d.ClassSession!.Studentid!))
            ?? throw new ArgumentException("Không tìm thấy tranh chấp cho buổi học này");

        if (dispute.Createdby != dispute.ClassSession?.Tutorid)
            throw new InvalidOperationException("Tranh chấp này không phải do gia sư tạo, bạn không thể phản hồi ở đây.");

        if (dispute.Status != DisputeStatus.Pending)
            throw new InvalidOperationException("Tranh chấp đã bước vào giai đoạn điều tra hoặc đã được giải quyết, không thể phản hồi thêm vào hồ sơ. Dùng kênh chat để trao đổi thêm với admin.");

        dispute.Respondentresponse = response;
        dispute.Respondentrespondedat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;
        await _context.SaveChangesAsync();

        _logger.LogInformation("User {UserId} submitted a respondent response to dispute {DisputeId}", userId, dispute.Disputeid);

        if (!string.IsNullOrEmpty(dispute.Createdby))
        {
            try
            {
                await _notificationService.CreateNotificationAsync(new NotificationRequest
                {
                    Userid = dispute.Createdby,
                    Title = "Đã có phản hồi cho khiếu nại",
                    Message = $"Phụ huynh/học sinh đã gửi phản hồi cho khiếu nại #{dispute.Disputeid}. Admin sẽ xem xét và xử lý.",
                    Type = NotificationType.DisputeResponded,
                    Referenceid = dispute.Classsessionid?.ToString()
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to notify dispute creator {CreatedBy} of respondent response for dispute {DisputeId}",
                    dispute.Createdby, dispute.Disputeid);
            }
        }

        return (await GetDisputeDetailAsync(dispute.Disputeid, userId))!;
    }

    /// <summary>Đối xứng với <see cref="UploadTutorDisputeEvidenceAsync"/>, dùng khi gia sư là người tạo dispute.</summary>
    public async Task<string> UploadRespondentDisputeEvidenceAsync(int classSessionId, string userId, string role, IFormFile file)
    {
        if (file == null || file.Length == 0)
            throw new ArgumentException("Tệp bằng chứng là bắt buộc.");

        var studentIds = role == UserRole.Parent
            ? await _context.Studentprofiles.Where(s => s.Parentid == userId).Select(s => s.Studentid).ToListAsync()
            : await _context.Studentprofiles.Where(s => s.Studentid == userId || s.Linkeduserid == userId).Select(s => s.Studentid).ToListAsync();

        var dispute = await _context.Disputes
            .Include(d => d.ClassSession)
            .FirstOrDefaultAsync(d => d.Classsessionid == classSessionId && studentIds.Contains(d.ClassSession!.Studentid!))
            ?? throw new ArgumentException("Không tìm thấy tranh chấp cho buổi học này");

        if (dispute.Createdby != dispute.ClassSession?.Tutorid)
            throw new InvalidOperationException("Tranh chấp này không phải do gia sư tạo, bạn không thể nộp bằng chứng ở đây.");

        if (dispute.Status != DisputeStatus.Pending)
            throw new InvalidOperationException("Tranh chấp đã bước vào giai đoạn điều tra hoặc đã được giải quyết, không thể nộp thêm bằng chứng vào hồ sơ. Dùng kênh chat để trao đổi thêm với admin.");

        await _storageService.EnsureBucketExistsAsync(StorageBucket.ClassSessionAttachments);
        var folderPath = $"dispute-evidence-{classSessionId}";
        var fileUrl = await _storageService.UploadFileAsync(StorageBucket.ClassSessionAttachments, folderPath, file);

        _context.DisputeEvidences.Add(new DisputeEvidence
        {
            Disputeid = dispute.Disputeid,
            Uploadedby = userId,
            Fileurl = fileUrl,
            Filetype = file.ContentType,
            Createdat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow
        });
        await _context.SaveChangesAsync();

        return fileUrl;
    }

    // ── Dispute chat threads — private per-party channels with admin ───────────
    // Two independent threads per dispute (tutor<->admin, parent/student<->admin); neither
    // party sees the other's messages. Stays open through Investigating (that's the point —
    // it's the release valve once the formal report/evidence gets locked at that stage) and
    // closes once the dispute is Resolved/Closed.

    private async Task<List<DisputeMessageResponse>> MapDisputeMessagesAsync(List<DisputeMessage> messages)
    {
        var senderIds = messages.Select(m => m.Senderid).Where(id => !string.IsNullOrEmpty(id)).Distinct().ToList();
        var senderNames = await _context.Users
            .Where(u => senderIds.Contains(u.Userid))
            .ToDictionaryAsync(u => u.Userid, u => u.Fullname);

        return messages
            .OrderBy(m => m.Createdat)
            .Select(m => new DisputeMessageResponse
            {
                DisputeMessageId = m.Disputemessageid,
                DisputeId = m.Disputeid,
                ThreadType = m.Threadtype,
                SenderId = m.Senderid,
                SenderName = !string.IsNullOrEmpty(m.Senderid) && senderNames.TryGetValue(m.Senderid, out var name) ? name : null,
                SenderRole = m.Senderrole,
                Message = m.Message,
                CreatedAt = m.Createdat
            })
            .ToList();
    }

    /// <summary>Admin view of either thread for a dispute.</summary>
    public async Task<List<DisputeMessageResponse>> GetDisputeThreadAsync(int disputeId, string threadType)
    {
        var messages = await _context.DisputeMessages
            .Where(m => m.Disputeid == disputeId && m.Threadtype == threadType)
            .ToListAsync();
        return await MapDisputeMessagesAsync(messages);
    }

    public async Task<DisputeMessageResponse> SendAdminDisputeMessageAsync(int disputeId, string adminId, string threadType, string message)
    {
        var dispute = await _context.Disputes.Include(d => d.ClassSession).FirstOrDefaultAsync(d => d.Disputeid == disputeId)
            ?? throw new ArgumentException("Không tìm thấy tranh chấp");

        if (dispute.Status == DisputeStatus.Resolved || dispute.Status == DisputeStatus.Closed)
            throw new InvalidOperationException("Tranh chấp đã được giải quyết, không thể nhắn thêm");

        var entity = new DisputeMessage
        {
            Disputeid = disputeId,
            Threadtype = threadType,
            Senderid = adminId,
            Senderrole = "admin",
            Message = message,
            Createdat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow
        };
        _context.DisputeMessages.Add(entity);
        await _context.SaveChangesAsync();

        var response = (await MapDisputeMessagesAsync(new List<DisputeMessage> { entity })).First();

        var recipientId = threadType == DisputeThreadType.Tutor ? dispute.ClassSession?.Tutorid : dispute.Createdby;
        if (!string.IsNullOrEmpty(recipientId))
        {
            await NotifyAndBroadcastDisputeMessageAsync(
                recipientId, response, "Admin nhắn tin về tranh chấp", $"Admin đã gửi tin nhắn cho bạn về tranh chấp #{disputeId}.");
        }

        return response;
    }

    /// <summary>Broadcasts a dispute chat message live to the recipient's SignalR group (so an
    /// already-open thread can splice it in without refetching) and creates a DB notification
    /// (badge + bell, and — via NotificationService — its own real-time push).</summary>
    private async Task NotifyAndBroadcastDisputeMessageAsync(string recipientId, DisputeMessageResponse response, string title, string notificationMessage)
    {
        try
        {
            await _hubContext.Clients.Group($"user:{recipientId}").SendAsync("disputeMessageReceived", response);
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Failed to broadcast dispute message to {RecipientId} for dispute {DisputeId}", recipientId, response.DisputeId); }

        try
        {
            await _notificationService.CreateNotificationAsync(new NotificationRequest
            {
                Userid = recipientId,
                Title = title,
                Message = notificationMessage,
                Type = NotificationType.DisputeMessage,
                Referenceid = response.DisputeId.ToString()
            });
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Failed to notify {RecipientId} of dispute message for dispute {DisputeId}", recipientId, response.DisputeId); }
    }

    /// <summary>Tutor's own view of their thread for a classSession's dispute.</summary>
    public async Task<List<DisputeMessageResponse>> GetTutorDisputeThreadAsync(int classSessionId, string tutorId)
    {
        var disputeId = await _context.Disputes
            .Where(d => d.Classsessionid == classSessionId && d.ClassSession!.Tutorid == tutorId)
            .Select(d => (int?)d.Disputeid)
            .FirstOrDefaultAsync();
        if (!disputeId.HasValue) return new List<DisputeMessageResponse>();

        return await GetDisputeThreadAsync(disputeId.Value, DisputeThreadType.Tutor);
    }

    public async Task<DisputeMessageResponse> SendTutorDisputeMessageAsync(int classSessionId, string tutorId, string message)
    {
        var dispute = await _context.Disputes
            .FirstOrDefaultAsync(d => d.Classsessionid == classSessionId && d.ClassSession!.Tutorid == tutorId)
            ?? throw new ArgumentException("Không tìm thấy tranh chấp cho buổi học này");

        if (dispute.Status == DisputeStatus.Resolved || dispute.Status == DisputeStatus.Closed)
            throw new InvalidOperationException("Tranh chấp đã được giải quyết, không thể nhắn thêm");

        var entity = new DisputeMessage
        {
            Disputeid = dispute.Disputeid,
            Threadtype = DisputeThreadType.Tutor,
            Senderid = tutorId,
            Senderrole = "tutor",
            Message = message,
            Createdat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow
        };
        _context.DisputeMessages.Add(entity);
        await _context.SaveChangesAsync();

        var response = (await MapDisputeMessagesAsync(new List<DisputeMessage> { entity })).First();
        await NotifyAdminsOfDisputeMessageAsync(response);
        return response;
    }

    /// <summary>Notify every dispute handler of a new tutor/parent dispute message — there's no
    /// single "assigned" admin per dispute, so all of them get the badge/real-time push.
    /// Bao gồm cả Staff có dispute.view, không chỉ Admin.</summary>
    private async Task NotifyAdminsOfDisputeMessageAsync(DisputeMessageResponse response)
    {
        var adminIds = await PermissionRecipients.ResolveAsync(_context, Permissions.DisputeView);

        foreach (var adminId in adminIds)
        {
            await NotifyAndBroadcastDisputeMessageAsync(
                adminId, response, "Tin nhắn mới trong tranh chấp", $"Có tin nhắn mới trong tranh chấp #{response.DisputeId}.");
        }
    }

    /// <summary>Parent/student's own view of their thread for a classSession's dispute.</summary>
    public async Task<List<DisputeMessageResponse>> GetPartyDisputeThreadAsync(int classSessionId, string userId, string role)
    {
        var studentIds = role == UserRole.Parent
            ? await _context.Studentprofiles.Where(s => s.Parentid == userId).Select(s => s.Studentid).ToListAsync()
            : await _context.Studentprofiles.Where(s => s.Studentid == userId || s.Linkeduserid == userId).Select(s => s.Studentid).ToListAsync();

        var disputeId = await _context.Disputes
            .Where(d => d.Classsessionid == classSessionId && studentIds.Contains(d.ClassSession!.Studentid!))
            .Select(d => (int?)d.Disputeid)
            .FirstOrDefaultAsync();
        if (!disputeId.HasValue) return new List<DisputeMessageResponse>();

        return await GetDisputeThreadAsync(disputeId.Value, DisputeThreadType.Parent);
    }

    public async Task<DisputeMessageResponse> SendPartyDisputeMessageAsync(int classSessionId, string userId, string role, string message)
    {
        var studentIds = role == UserRole.Parent
            ? await _context.Studentprofiles.Where(s => s.Parentid == userId).Select(s => s.Studentid).ToListAsync()
            : await _context.Studentprofiles.Where(s => s.Studentid == userId || s.Linkeduserid == userId).Select(s => s.Studentid).ToListAsync();

        var dispute = await _context.Disputes
            .FirstOrDefaultAsync(d => d.Classsessionid == classSessionId && studentIds.Contains(d.ClassSession!.Studentid!))
            ?? throw new ArgumentException("Không tìm thấy tranh chấp cho buổi học này");

        if (dispute.Status == DisputeStatus.Resolved || dispute.Status == DisputeStatus.Closed)
            throw new InvalidOperationException("Tranh chấp đã được giải quyết, không thể nhắn thêm");

        var entity = new DisputeMessage
        {
            Disputeid = dispute.Disputeid,
            Threadtype = DisputeThreadType.Parent,
            Senderid = userId,
            Senderrole = role,
            Message = message,
            Createdat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow
        };
        _context.DisputeMessages.Add(entity);
        await _context.SaveChangesAsync();

        var response = (await MapDisputeMessagesAsync(new List<DisputeMessage> { entity })).First();
        await NotifyAdminsOfDisputeMessageAsync(response);
        return response;
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
