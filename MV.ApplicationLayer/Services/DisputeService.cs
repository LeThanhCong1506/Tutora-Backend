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
    private readonly IZaloOAService _zaloOAService;
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
        IZaloOAService zaloOAService,
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
        _zaloOAService = zaloOAService;
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
        if (query.ClassSessionId.HasValue)
            q = q.Where(d => d.Classsessionid == query.ClassSessionId.Value);

        // Thứ tự phải quyết định ở đây vì danh sách phân trang ở server — sắp xếp
        // sau khi đã cắt trang chỉ đảo được đúng trang đang xem.
        q = ListSortDirection.IsAscending(query.SortDirection)
            ? q.OrderBy(d => d.Createdat)
            : q.OrderByDescending(d => d.Createdat);

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

        return new PagedList<DisputeListResponse>(disputes, totalCount, query.Page, query.PageSize);
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
    /// Runs AI (Groq) priority classification for a dispute and persists the result. Never throws on
    /// classification failure — the dispute simply stays unclassified so it can be retried later.
    /// actorId scopes the returned DisputeDetailResponse's recording stream token; pass "system" for
    /// the background (Hangfire) trigger, where the response is discarded anyway.
    /// </summary>
    public async Task<DisputeDetailResponse?> ClassifyDisputePriorityAsync(int disputeId, string actorId)
    {
        var dispute = await _disputeRepo.FindWithClassSessionAsync(disputeId);
        if (dispute == null)
        {
            _logger.LogWarning("ClassifyDisputePriorityAsync: dispute {DisputeId} not found", disputeId);
            return null;
        }

        _logger.LogInformation("ClassifyDisputePriorityAsync started for dispute {DisputeId} (type={DisputeType})", disputeId, dispute.Disputetype);

        var classification = await _classificationService.ClassifyAsync(dispute.Disputetype ?? "", dispute.Reason ?? "");
        dispute.Priority = classification.Priority;
        dispute.Priorityreason = classification.Reason;
        await _disputeRepo.SaveChangesAsync();
        _logger.LogInformation("Dispute {DisputeId} classified as priority {Priority}", disputeId, classification.Priority);

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
                        Type = NotificationType.LessonNoShow,
                        Referenceid = classSessionId.ToString()
                    });
                if (!string.IsNullOrWhiteSpace(tutorId))
                    notifications.Add(new NotificationRequest
                    {
                        Userid = tutorId,
                        Title = "Xác nhận vắng mặt",
                        Message = $"Admin đã xác nhận báo cáo vắng mặt cho buổi học #{classSessionId}.",
                        Type = NotificationType.LessonNoShow,
                        Referenceid = classSessionId.ToString()
                    });
                if (notifications.Count > 0)
                    await _notificationService.CreateNotificationsAsync(notifications);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send no-show confirmation notifications for dispute {DisputeId}", disputeId);
            }

            if (!string.IsNullOrWhiteSpace(snapshot.Createdby))
            {
                try
                {
                    await _zaloOAService.SendNotificationAsync(
                        snapshot.Createdby,
                        ZnsTemplateType.DisputeResult,
                        new Dictionary<string, string>
                        {
                            { "mon_hoc", "" },
                            { "ket_qua", $"Admin đã xác nhận gia sư vắng mặt ở buổi học #{classSessionId}. Bạn có thể chọn phương án xử lý." },
                            { "so_tien_hoan", "0" }
                        });
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to send ZNS no-show confirmation for dispute {DisputeId}", disputeId);
                }
            }
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
            .Select(d => new { d.Bookingid })
            .FirstOrDefaultAsync()
            ?? throw new ArgumentException("Không tìm thấy tranh chấp");

        string? createdBy;
        string? tutorId;
        decimal amountRefunded = 0;

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
                    _ => 0
                };

                if (classSession != null)
                {
                    if (refundPercentage > 0)
                    {
                        var refundResult = await _settlementService.ProcessRefundAsync(classSession.Classsessionid, refundPercentage, adminId);
                        amountRefunded = refundResult.AmountRefunded;
                    }
                    else
                        await _settlementService.SettleDisputedClassSessionAsync(classSession.Classsessionid, adminId);
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
                notifications.Add(new NotificationRequest
                {
                    Userid = createdBy,
                    Title = "Tranh chấp đã được giải quyết",
                    Message = $"Tranh chấp #{disputeId} đã được giải quyết. Kết quả: {request.ResolutionType}. Ghi chú: {request.ResolutionNote}",
                    Type = NotificationType.DisputeResolved,
                    Referenceid = disputeId.ToString()
                });

            if (tutorId != null)
                notifications.Add(new NotificationRequest { Userid = tutorId, Title = "Thông báo giải quyết tranh chấp",
                    Message = $"Tranh chấp #{disputeId} liên quan đến buổi học của bạn đã được giải quyết. Kết quả: {request.ResolutionType}.",
                    Type = NotificationType.DisputeResolved,
                    Referenceid = disputeId.ToString() });

            await _notificationService.CreateNotificationsAsync(notifications);

            if (!string.IsNullOrWhiteSpace(createdBy))
            {
                try
                {
                    await _zaloOAService.SendNotificationAsync(
                        createdBy,
                        ZnsTemplateType.DisputeResult,
                        new Dictionary<string, string>
                        {
                            { "mon_hoc", "" },
                            { "ket_qua", $"Đã giải quyết: {request.ResolutionNote}" },
                            { "so_tien_hoan", amountRefunded.ToString("N0") }
                        });
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to send ZNS dispute-resolved notification for dispute {DisputeId}", disputeId);
                }
            }

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

    public async Task<PagedList<DisputeListResponse>> GetTutorDisputesAsync(string tutorId, int page, int pageSize)
    {
        var query = _context.Disputes
            .AsNoTracking()
            .Where(d => d.ClassSession!.Tutorid == tutorId)
            .OrderByDescending(d => d.Createdat);

        var totalCount = await query.CountAsync();

        var disputes = await query
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
                    Message = $"Gia sư đã gửi phản hồi cho khiếu nại #{dispute.Disputeid}. Admin sẽ xem xét và xử lý."
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
        var dispute = await _context.Disputes
            .FirstOrDefaultAsync(d => d.Classsessionid == classSessionId && d.ClassSession!.Tutorid == tutorId)
            ?? throw new ArgumentException("Không tìm thấy tranh chấp cho buổi học này");

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

    /// <summary>Notify every admin of a new tutor/parent dispute message — there's no single
    /// "assigned" admin per dispute, so all of them get the badge/real-time push.</summary>
    private async Task NotifyAdminsOfDisputeMessageAsync(DisputeMessageResponse response)
    {
        var adminIds = await _context.Users
            .Where(u => u.Primaryrole == UserRole.Admin)
            .Select(u => u.Userid)
            .ToListAsync();

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
