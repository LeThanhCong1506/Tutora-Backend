using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using MV.ApplicationLayer.Helpers;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.Entities;
using MV.DomainLayer.Exceptions;
using MV.DomainLayer.Helpers;
using static MV.DomainLayer.Constants.ClassSessionStatus;

namespace MV.ApplicationLayer.Services;

public partial class ClassSessionService
{
    // ── M3-T7: No-show Handling ───────────────────────────────────────────────

    /// <summary>
    /// Gia sư còn trong thời gian được coi là "có thể đang vào lớp trễ" — báo vắng mặt chỉ mở
    /// sau khi buổi đã trễ chừng này so với giờ bắt đầu (khôi phục lại ràng buộc gốc, bị gỡ ở
    /// commit 7f509cf theo một quyết định sản phẩm hoá ra lại cho báo vắng mặt được cả TRƯỚC giờ học).
    /// </summary>
    public const int NoShowReportEarliestMinutes = 15;

    public async Task<ClassSessionDetailResponse> ReportTutorNoShowAsync(int classSessionId, string userId, string role, ReportNoShowRequest? request = null)
    {
        var studentIds = role == UserRole.Parent
            ? await _context.Studentprofiles.Where(s => s.Parentid == userId).Select(s => s.Studentid).ToListAsync()
            : await _context.Studentprofiles.Where(s => s.Studentid == userId || s.Linkeduserid == userId).Select(s => s.Studentid).ToListAsync();

        var ownedSession = await _context.ClassSessions
            .AsNoTracking()
            .Include(l => l.Booking)
            .FirstOrDefaultAsync(l => l.Classsessionid == classSessionId && studentIds.Contains(l.Studentid!))
            ?? throw new ClassSessionException(ClassSessionErrorCodes.ClassSessionNotFound, "Không tìm thấy buổi học hoặc bạn không có quyền truy cập", 404);

        if (role == UserRole.Student)
        {
            var studentProfile = await _context.Studentprofiles.FirstOrDefaultAsync(s => s.Studentid == userId || s.Linkeduserid == userId);
            if (studentProfile != null && studentProfile.Parentid != null)
                throw new ClassSessionException(BookingErrorCodes.StudentManagedByParent, "Tài khoản học sinh do phụ huynh quản lý không thể tự báo cáo vắng mặt", 403);
        }

        // Fast-fail trước khi upload evidence (UX) — nguồn sự thật thật sự là re-check sau khi
        // lock bên dưới, vì đọc không lock ở đây có thể đã cũ (stale) so với lúc ghi thật.
        if (ownedSession.Status != Scheduled)
            throw new ClassSessionException(ClassSessionErrorCodes.InvalidClassSessionStatus, "Buổi học không ở trạng thái đã lên lịch", 400);
        if (DisputeSettlementPolicy.IsTerminalBooking(ownedSession.Booking?.Status))
            throw new ClassSessionException(ClassSessionErrorCodes.InvalidClassSessionStatus, "Booking đã kết thúc, không thể tạo báo cáo mới", 400);

        var now = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;

        if ((now - ownedSession.Scheduledstart).TotalMinutes < NoShowReportEarliestMinutes)
            throw new ClassSessionException(
                ClassSessionErrorCodes.TooEarlyToReportNoShow,
                $"Chỉ có thể báo cáo vắng mặt sau {NoShowReportEarliestMinutes} phút kể từ giờ bắt đầu",
                400);

        // Reported time is advisory context folded into the dispute reason text — it's not
        // compared against Scheduledstart; only the real server clock (now, gated above) decides
        // when reporting opens.
        var reportedAt = request?.ReportedAt ?? now;
        var reasonText = !string.IsNullOrWhiteSpace(request?.Reason)
            ? $"Tutor no-show lúc {reportedAt:dd/MM/yyyy HH:mm}: {request!.Reason}"
            : $"Tutor no-show: Gia sư không có mặt lúc {reportedAt:dd/MM/yyyy HH:mm}";

        var uploadedEvidence = new List<string>();
        var evidenceFolder = $"dispute-evidence-{classSessionId}";
        Dispute dispute;
        ClassSession classSession;

        await using var tx = await _context.Database.BeginTransactionAsync();
        try
        {
            if (request?.Files?.Count > 0)
            {
                await _storageService.EnsureBucketExistsAsync(ClassSessionAttachmentBucket);
                foreach (var file in request.Files.Where(file => file is { Length: > 0 }))
                {
                    uploadedEvidence.Add(await _storageService.UploadFileAsync(
                        ClassSessionAttachmentBucket,
                        evidenceFolder,
                        file));
                }
            }

            // Lock + re-check ngay trước khi ghi Status — tránh đè lên claim "chỉ mình gia sư có
            // mặt" nếu SubmitReportAsync (nhánh solo tutor no-show) vừa commit trước trên đúng
            // buổi này (2 actor có thể cùng nhắm vào field Status của 1 session không có
            // concurrency token, xem ClassSessionService.M3.Attendance.cs).
            classSession = await ClassSessionLockHelper.LockById(_context, classSessionId)
                .Include(l => l.Booking)
                .SingleOrDefaultAsync()
                ?? throw new ClassSessionException(ClassSessionErrorCodes.ClassSessionNotFound, "Không tìm thấy buổi học", 404);

            if (classSession.Status != Scheduled)
                throw new ClassSessionException(ClassSessionErrorCodes.InvalidClassSessionStatus, "Buổi học không còn ở trạng thái đã lên lịch (có thể vừa được xử lý bởi luồng khác)", 400);
            if (DisputeSettlementPolicy.IsTerminalBooking(classSession.Booking?.Status))
                throw new ClassSessionException(ClassSessionErrorCodes.InvalidClassSessionStatus, "Booking đã kết thúc, không thể tạo báo cáo mới", 400);

            classSession.Status = NoShow;
            classSession.Istutorpresent = false;

            // Auto-create dispute record to track no-show, including evidence in the same request.
            dispute = new Dispute
            {
                Classsessionid = classSessionId,
                Bookingid = classSession.Bookingid,
                Createdby = userId,
                Disputetype = DisputeTypes.NoShow,
                Reason = reasonText,
                Status = DisputeStatus.Pending,
                Evidence = uploadedEvidence.Count > 0 ? JsonSerializer.Serialize(uploadedEvidence) : null,
                Createdat = now
            };
            _context.Disputes.Add(dispute);

            await _context.SaveChangesAsync();
            await tx.CommitAsync();
        }
        catch
        {
            await tx.RollbackAsync();
            foreach (var fileUrl in uploadedEvidence)
            {
                try
                {
                    await _storageService.DeleteFileAsync(
                        ClassSessionAttachmentBucket,
                        evidenceFolder,
                        fileUrl);
                }
                catch (Exception cleanupError)
                {
                    _logger.LogWarning(
                        cleanupError,
                        "Failed to clean orphan no-show evidence {FileUrl} for classSession {ClassSessionId}",
                        fileUrl,
                        classSessionId);
                }
            }

            throw;
        }

        try
        {
            var jobId = _backgroundJobClient.Enqueue<IDisputeService>(
                s => s.ClassifyDisputePriorityAsync(dispute.Disputeid, "system", true));
            _logger.LogInformation(
                "Enqueued Hangfire job {JobId} to classify priority for dispute {DisputeId}",
                jobId,
                dispute.Disputeid);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enqueue priority classification job for dispute {DisputeId}", dispute.Disputeid);
        }

        // Notify tutor about the no-show report
        if (!string.IsNullOrEmpty(classSession.Tutorid))
        {
            try
            {
                await _notificationService.CreateNotificationAsync(new NotificationRequest
                {
                    Userid = classSession.Tutorid,
                    Title = "Báo cáo vắng mặt",
                    Message = $"Bạn đã bị báo cáo vắng mặt cho buổi học #{classSessionId}.",
                    Type = NotificationType.LessonNoShow,
                    Referenceid = classSessionId.ToString()
                });
            }
            catch (Exception notificationError)
            {
                _logger.LogWarning(
                    notificationError,
                    "No-show dispute {DisputeId} was created but tutor notification failed",
                    dispute.Disputeid);
            }
        }

        _logger.LogInformation("User {UserId} ({Role}) reported tutor no-show for classSession {ClassSessionId}, dispute {DisputeId} created", userId, role, classSessionId, dispute.Disputeid);
        return MapToClassSessionDetailResponse(classSession);
    }

    public async Task<NoShowActionResultResponse> ProcessNoShowActionAsync(int classSessionId, string userId, string role, NoShowActionRequest request)
    {
        if (!NoShowActionTypes.All.Contains(request.ActionType))
            throw new ClassSessionException(ClassSessionErrorCodes.InvalidNoShowAction, "Hành động xử lý vắng mặt không hợp lệ", 400);

        // Pre-tx: ownership + fast-fail status check (stale read OK here)
        var studentIds = role == UserRole.Parent
            ? await _context.Studentprofiles.Where(s => s.Parentid == userId).Select(s => s.Studentid).ToListAsync()
            : await _context.Studentprofiles.Where(s => s.Studentid == userId || s.Linkeduserid == userId).Select(s => s.Studentid).ToListAsync();

        var ownedSession = await _context.ClassSessions
            .AsNoTracking()
            .Include(l => l.Booking)
            .FirstOrDefaultAsync(l => l.Classsessionid == classSessionId && studentIds.Contains(l.Studentid!))
            ?? throw new ClassSessionException(ClassSessionErrorCodes.ClassSessionNotFound, "Không tìm thấy buổi học hoặc bạn không có quyền truy cập", 404);

        if (role == UserRole.Student)
        {
            var studentProfile = await _context.Studentprofiles.FirstOrDefaultAsync(s => s.Studentid == userId || s.Linkeduserid == userId);
            if (studentProfile != null && studentProfile.Parentid != null)
                throw new ClassSessionException(BookingErrorCodes.StudentManagedByParent, "Tài khoản học sinh do phụ huynh quản lý không thể tự xử lý vắng mặt", 403);
        }

        if (ownedSession.Status != NoShow)
            throw new ClassSessionException(ClassSessionErrorCodes.InvalidClassSessionStatus, "Buổi học không ở trạng thái vắng mặt", 400);

        var result = new NoShowActionResultResponse { ClassSessionId = classSessionId, ActionType = request.ActionType, Success = true };

        await using var tx = await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        try
        {
            if (!ownedSession.Bookingid.HasValue)
                throw new InvalidOperationException($"Booking for classSession {classSessionId} not found");

            // Same lock order as admin no-show confirmation: booking -> dispute -> class session -> wallets.
            _ = await _context.Bookings
                .FromSqlRaw(SqlQueries.LockBookingById, ownedSession.Bookingid.Value)
                .AsNoTracking()
                .SingleOrDefaultAsync()
                ?? throw new InvalidOperationException($"Booking {ownedSession.Bookingid.Value} not found");

            var noShowDisputeId = await _context.Disputes
                .AsNoTracking()
                .Where(d => d.Classsessionid == classSessionId && d.Disputetype == DisputeTypes.NoShow)
                .OrderByDescending(d => d.Disputeid)
                .Select(d => (int?)d.Disputeid)
                .FirstOrDefaultAsync()
                ?? throw new ClassSessionException(ClassSessionErrorCodes.InvalidNoShowAction, "Không tìm thấy báo cáo vắng mặt để xử lý", 409);

            var noShowDispute = await _context.Disputes
                .FromSqlRaw(SqlQueries.LockDisputeById, noShowDisputeId)
                .SingleOrDefaultAsync()
                ?? throw new ClassSessionException(ClassSessionErrorCodes.InvalidNoShowAction, "Không tìm thấy báo cáo vắng mặt để xử lý", 409);

            if (noShowDispute.Status != DisputeStatus.ConfirmedNoShow
                || !noShowDispute.Noshowconfirmedat.HasValue
                || string.IsNullOrWhiteSpace(noShowDispute.Noshowconfirmedby))
                throw new ClassSessionException(
                    ClassSessionErrorCodes.InvalidNoShowAction,
                    "Báo cáo vắng mặt chưa được admin xác nhận. Vui lòng chờ kết quả kiểm tra.",
                    409);

            var classSession = await ClassSessionLockHelper.LockById(_context, classSessionId)
                .Include(l => l.Booking)
                    .ThenInclude(b => b!.Student)
                .SingleOrDefaultAsync()
                ?? throw new ClassSessionException(ClassSessionErrorCodes.ClassSessionNotFound, "Không tìm thấy buổi học", 404);

            if (classSession.Issettled == true)
                throw new ClassSessionException(ClassSessionErrorCodes.ClassSessionAlreadyConfirmed, "Buổi học này đã được xử lý rồi", 400);
            if (classSession.Status != NoShow)
                throw new ClassSessionException(ClassSessionErrorCodes.InvalidClassSessionStatus, "Buổi học không còn ở trạng thái no-show", 400);

            var booking = classSession.Booking
                ?? throw new InvalidOperationException($"Booking for classSession {classSessionId} not found");

            var parentRefundPerSession = LessonRefundCalculator.ParentRefundPerSession(booking);
            var tutorEscrowPerSession = LessonRefundCalculator.TutorEscrowPerSession(booking);
            var now = TimeZoneHelper.UtcNow;
            var refundRecipientId = !string.IsNullOrWhiteSpace(booking.Parentid)
                ? booking.Parentid
                : (!string.IsNullOrWhiteSpace(booking.Student?.Linkeduserid)
                    ? booking.Student.Linkeduserid
                    : booking.Studentid);

            var tutorWallet = !string.IsNullOrWhiteSpace(classSession.Tutorid)
                ? await _context.Wallets
                    .FromSqlRaw(SqlQueries.LockWalletByUserId, classSession.Tutorid)
                    .FirstOrDefaultAsync()
                : null;
            var parentWallet = !string.IsNullOrWhiteSpace(refundRecipientId)
                ? await WalletLockHelper.GetOrCreateForUpdateAsync(_context, refundRecipientId, now)
                : null;

            classSession.Noshowaction = request.ActionType;

            switch (request.ActionType)
            {
                case NoShowActionTypes.FreeSession:
                    // Delegate to the same 100%-refund path dispute resolution uses (release=refund_100):
                    // handles the escrow/refund math, decrements Booking.Sessionsremaining, and releases
                    // any remaining held escrow once the booking's last session is resolved. Runs inside
                    // this method's already-open transaction (ownsTx=false inside), so it neither opens a
                    // nested transaction nor sends its own notification — this method still controls both.
                    var freeSessionResult = await _settlementService.ProcessRefundAsync(classSessionId, 100, userId);
                    result.AmountRefunded = freeSessionResult.AmountRefunded;
                    result.Message = "Buổi học đã được hủy và hoàn tiền 100%";
                    break;

                case NoShowActionTypes.Makeup:
                    if (!request.NewScheduledStart.HasValue)
                        throw new ClassSessionException(ClassSessionErrorCodes.MakeupTimeRequired, "Vui lòng cung cấp thời gian học bù mới", 400);
                    var makeupClassSession = await CreateMakeupClassSessionAsync(classSessionId, request.NewScheduledStart.Value, classSession.Tutorid!);
                    // Mark original resolved so a repeated no-show-action is blocked by the idempotency guard.
                    // Escrow of the original stays frozen until the makeup is settled (known issue #10).
                    classSession.Issettled = true;
                    result.MakeupClassSessionId = makeupClassSession.ClassSessionId;
                    result.Message = $"Buổi học bù đã được tạo vào {request.NewScheduledStart:dd/MM/yyyy HH:mm}";
                    break;

                case NoShowActionTypes.ChangeTutor:
                    classSession.Status = Cancelled;
                    classSession.Issettled = true;

                    var remaining = booking.Sessionsremaining ?? 0;

                    // Clamp parent refund against what was actually paid minus any prior booking refunds
                    decimal totalPaidByParent = booking.Remainingpaidat.HasValue
                        ? (booking.Finalprice ?? 0)
                        : (booking.Depositpaidat.HasValue ? (booking.Depositamount ?? 0) : 0m);
                    var totalAlreadyRefunded = await _context.Wallettransactions
                        .Where(wt => wt.Referencetable == ReferenceTable.Booking
                                  && wt.Referenceid == classSession.Bookingid
                                  && wt.Transactiontype == TransactionType.Refund)
                        .SumAsync(wt => wt.Amount ?? 0);
                    var maxParentRefund = Math.Max(0, totalPaidByParent - totalAlreadyRefunded);
                    var parentTotalRefund = Math.Round(Math.Min(remaining * parentRefundPerSession, maxParentRefund), 2);

                    // Clamp tutor escrow release against actual frozen balance
                    var tutorEscrowRelease = Math.Round(
                        Math.Min(remaining * tutorEscrowPerSession, Math.Max(0, tutorWallet?.Frozenbalance ?? 0)), 2);

                    if (tutorWallet != null && tutorEscrowRelease > 0)
                    {
                        tutorWallet.Frozenbalance = Math.Max(0, (tutorWallet.Frozenbalance ?? 0) - tutorEscrowRelease);
                        tutorWallet.Lastupdated = now;
                        _context.Wallettransactions.Add(new Wallettransaction
                        {
                            Walletid = tutorWallet.Walletid,
                            Amount = -tutorEscrowRelease,
                            Transactiontype = TransactionType.EscrowReversal,
                            Referencetable = ReferenceTable.Booking,
                            Referenceid = classSession.Bookingid,
                            Description = $"Giải phóng escrow no-show change tutor - booking #{classSession.Bookingid} ({remaining} buổi còn lại)",
                            Createdat = now
                        });
                    }

                    if (parentWallet != null && parentTotalRefund > 0)
                    {
                        parentWallet.Balance += parentTotalRefund;
                        parentWallet.Lastupdated = now;
                        _context.Wallettransactions.Add(new Wallettransaction
                        {
                            Walletid = parentWallet.Walletid,
                            Amount = parentTotalRefund,
                            Transactiontype = TransactionType.Refund,
                            Referencetable = ReferenceTable.Booking,
                            Referenceid = classSession.Bookingid,
                            Description = $"Hoàn tiền no-show change tutor - booking #{classSession.Bookingid} ({remaining} buổi còn lại)",
                            Createdat = now
                        });
                    }

                    // Cancel all other Scheduled/Reserved classSessions in this booking
                    var futureClassSessions = await _context.ClassSessions
                        .Where(l => l.Bookingid == classSession.Bookingid
                                 && l.Classsessionid != classSessionId
                                 && (l.Status == Scheduled || l.Status == Reserved))
                        .ToListAsync();
                    foreach (var fl in futureClassSessions)
                        fl.Status = Cancelled;

                    booking.Status = BookingStatus.CancelledNoshow;
                    booking.Sessionsremaining = 0;
                    booking.Escrowstatus = EscrowStatus.Refunded;
                    // Đây là nhánh huỷ DUY NHẤT trước đây không ghi Cancelledat. Thiếu mốc này
                    // thì dòng thời gian booking bên CMS để trống ô "Booking cancelled", và báo
                    // cáo doanh thu không quy được khoản Tutora giữ lại về đúng kỳ — nó phải
                    // lùi về ngày tạo booking, có thể rơi sang kỳ khác.
                    booking.Cancelledat = now;

                    // Return the promotion usage consumed at booking creation (booking is being cancelled)
                    await MV.ApplicationLayer.Helpers.PromotionUsageHelper.ReturnUsageAsync(_context, booking.Promotionid);

                    result.AmountRefunded = parentTotalRefund;
                    result.Message = "Đã hủy booking và hoàn tiền các buổi còn lại";
                    break;
            }

            // Admin has already confirmed fault. The payer side now chooses the remedy and closes
            // the verified dispute atomically with the corresponding financial/session changes.
            var disputeRefundPercentage = request.ActionType == NoShowActionTypes.Makeup ? 0 : 100;
            noShowDispute.Status = DisputeStatus.Resolved;
            noShowDispute.Resolvedat = now;
            noShowDispute.Resolvedby = userId;
            noShowDispute.Resolutionnote = $"Người dùng tự xử lý ({request.ActionType}): {result.Message}";
            noShowDispute.Refundpercentage = disputeRefundPercentage;
            noShowDispute.Refundamount = result.AmountRefunded;
            noShowDispute.Refundissued = result.AmountRefunded > 0;

            // Makeup preserves the original escrow for a replacement session; the other remedies record fault,
            // so no warning. FreeSession/ChangeTutor mean the tutor genuinely no-showed.
            if (request.ActionType != NoShowActionTypes.Makeup && !string.IsNullOrEmpty(classSession.Tutorid))
            {
                await _warningService.CreateWarningAsync(
                    classSession.Tutorid,
                    new CreateWarningRequest
                    {
                        WarningLevel = 1,
                        Reason = $"Gia sư vắng mặt buổi học #{classSessionId} — người dùng xử lý: {request.ActionType}",
                        RelatedBookingId = classSession.Bookingid
                    },
                    userId);
                result.WarningCreated = true;
            }

            await _context.SaveChangesAsync();
            await tx.CommitAsync();
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }

        // Notify tutor after commit (best-effort)
        try
        {
            if (!string.IsNullOrEmpty(ownedSession.Tutorid))
            {
                await _notificationService.CreateNotificationAsync(new NotificationRequest
                {
                    Userid = ownedSession.Tutorid,
                    Title = "Xử lý vắng mặt",
                    Type = NotificationType.LessonNoShow,
                    Message = $"'{request.ActionType}' đã được chọn cho buổi học #{classSessionId} bị vắng mặt.",
                    Referenceid = classSessionId.ToString()
                });
            }
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Failed to notify tutor for no-show action classSession {ClassSessionId}", classSessionId); }

        _logger.LogInformation("NoShow action {ActionType} processed for classSession {ClassSessionId} by user {UserId} ({Role})",
            request.ActionType, classSessionId, userId, role);
        return result;
    }

    /// <summary>
    /// Quét các buổi <c>scheduled</c> đã quá giờ kết thúc dự kiến + <see cref="ClassSessionService.LiveSessionAutoEndGraceMinutes"/>
    /// phút mà KHÔNG AI từng vào phòng (Istutorpresent VÀ Isstudentpresent đều chưa từng được ghi
    /// nhận true — chưa từng có 1 nhịp heartbeat/auto-check-in nào) và cũng không ai chủ động gọi
    /// <see cref="ReportTutorNoShowAsync"/>. Không có bước này, một buổi mà cả 2 phía đều lặng lẽ
    /// không tham gia sẽ kẹt vĩnh viễn ở Scheduled — không job/luồng nào khác quét trạng thái này.
    /// <para>
    /// QUYẾT ĐỊNH SẢN PHẨM (cần đội sản phẩm xác nhận lại): vì hệ thống không thể tự biết lỗi thuộc
    /// về bên nào, phương án chọn là tái dùng NGUYÊN VẸN luồng dispute no-show đã có (giống hệt khi
    /// phụ huynh/học viên chủ động báo cáo qua ReportTutorNoShowAsync) — Status → no_show, tạo
    /// Dispute (Pending) để admin xác nhận rồi 2 bên tự chọn 1 trong 3 remedy sẵn có
    /// (FreeSession/Makeup/ChangeTutor). Hệ thống KHÔNG tự phán quyết/hoàn tiền ở bước này.
    /// </para>
    /// Loại trừ buổi phụ (Iscontinuation): Scheduledend của nó chỉ là mốc ước tính lúc tạo, không
    /// phải cam kết giờ học thật (xem TryAutoCheckInAsync/AutoCloseExpiredLiveSessionsAsync).
    /// Dùng bởi background job. Trả về số buổi đã tự phát hiện no-show.
    /// </summary>
    public async Task<int> AutoReportMissedSessionsAsync(CancellationToken ct = default)
    {
        var now = TimeZoneHelper.UtcNow;
        var cutoff = now.AddMinutes(-LiveSessionAutoEndGraceMinutes);

        var missedSessions = await _context.ClassSessions
            .Include(l => l.Booking)
            .Where(l => l.Status == Scheduled
                && !l.Iscontinuation
                && l.Scheduledend <= cutoff
                && l.Istutorpresent != true
                && l.Isstudentpresent != true)
            .ToListAsync(ct);

        if (missedSessions.Count == 0) return 0;

        var reportedCount = 0;
        foreach (var ownedSession in missedSessions)
        {
            if (DisputeSettlementPolicy.IsTerminalBooking(ownedSession.Booking?.Status))
                continue;

            var classSessionId = ownedSession.Classsessionid;
            await using var tx = await _context.Database.BeginTransactionAsync();
            Dispute dispute;
            ClassSession classSession;
            try
            {
                // Lock + re-check: buổi có thể vừa được 1 bên chủ động báo cáo (ReportTutorNoShowAsync)
                // hoặc vừa check-in đúng lúc job này chạy tới.
                classSession = await ClassSessionLockHelper.LockById(_context, classSessionId)
                    .Include(l => l.Booking)
                    .SingleOrDefaultAsync();
                if (classSession == null
                    || classSession.Status != Scheduled
                    || classSession.Istutorpresent == true
                    || classSession.Isstudentpresent == true
                    || DisputeSettlementPolicy.IsTerminalBooking(classSession.Booking?.Status))
                {
                    await tx.RollbackAsync();
                    continue;
                }

                classSession.Status = NoShow;
                classSession.Istutorpresent = false;

                dispute = new Dispute
                {
                    Classsessionid = classSessionId,
                    Bookingid = classSession.Bookingid,
                    Createdby = SystemActors.System,
                    Disputetype = DisputeTypes.NoShow,
                    Reason = $"[Hệ thống tự phát hiện] Không ghi nhận được gia sư lẫn học viên vào phòng, đã quá {LiveSessionAutoEndGraceMinutes} phút sau giờ kết thúc dự kiến {classSession.Scheduledend:o}.",
                    Status = DisputeStatus.Pending,
                    Createdat = now
                };
                _context.Disputes.Add(dispute);

                await _context.SaveChangesAsync(ct);
                await tx.CommitAsync();
                reportedCount++;
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                _logger.LogError(ex, "Không thể tự phát hiện no-show 2 phía cho buổi học {ClassSessionId}", classSessionId);
                continue;
            }

            try
            {
                _backgroundJobClient.Enqueue<IDisputeService>(
                    s => s.ClassifyDisputePriorityAsync(dispute.Disputeid, SystemActors.System, true));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to enqueue priority classification job for auto-detected no-show dispute {DisputeId}", dispute.Disputeid);
            }

            try
            {
                var notifications = new List<NotificationRequest>();
                if (!string.IsNullOrWhiteSpace(classSession.Tutorid))
                    notifications.Add(new NotificationRequest
                    {
                        Userid = classSession.Tutorid,
                        Title = "Buổi học không có ai tham gia",
                        Message = $"Buổi học #{classSessionId} đã quá giờ kết thúc mà không ghi nhận được ai vào phòng. Hệ thống đã tự động ghi nhận vắng mặt để xử lý.",
                        Type = NotificationType.LessonNoShow,
                        Referenceid = classSessionId.ToString()
                    });
                var parentId = ownedSession.Booking?.Parentid;
                if (!string.IsNullOrWhiteSpace(parentId))
                    notifications.Add(new NotificationRequest
                    {
                        Userid = parentId,
                        Title = "Buổi học không có ai tham gia",
                        Message = $"Buổi học #{classSessionId} đã quá giờ kết thúc mà không ghi nhận được ai vào phòng. Hệ thống đã tự động ghi nhận vắng mặt để xử lý.",
                        Type = NotificationType.LessonNoShow,
                        Referenceid = classSessionId.ToString()
                    });
                if (notifications.Count > 0)
                    await _notificationService.CreateNotificationsAsync(notifications);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Không thể gửi thông báo tự phát hiện no-show cho buổi học {ClassSessionId}", classSessionId);
            }

            _logger.LogWarning("Hệ thống tự phát hiện no-show 2 phía cho buổi học {ClassSessionId}, dispute {DisputeId} đã tạo", classSessionId, dispute.Disputeid);
        }

        return reportedCount;
    }

    public async Task<ClassSessionDetailResponse> CreateMakeupClassSessionAsync(int originalClassSessionId, DateTime newScheduledStart, string tutorId)
    {
        var originalClassSession = await _context.ClassSessions
            .Include(l => l.Booking)
            .FirstOrDefaultAsync(l => l.Classsessionid == originalClassSessionId && l.Tutorid == tutorId)
            ?? throw new ClassSessionException(ClassSessionErrorCodes.ClassSessionNotFound, "Không tìm thấy buổi học gốc", 404);

        var duration = originalClassSession.Scheduledend - originalClassSession.Scheduledstart;

        // Frontend sends an ISO instant. Preserve UTC instants and treat an unspecified value as UTC
        // for backward compatibility with existing clients.
        var scheduledStartUtc = newScheduledStart.Kind == DateTimeKind.Utc
            ? newScheduledStart
            : DateTime.SpecifyKind(newScheduledStart, DateTimeKind.Utc);
        var scheduledEndUtc = scheduledStartUtc.Add(duration);

        if (scheduledStartUtc <= TimeZoneHelper.UtcNow)
            throw new ClassSessionException(ClassSessionErrorCodes.MakeupTimeRequired, "Thời gian học bù phải ở tương lai", 400);

        var hasTutorConflict = await HasTutorSchedulingConflictAsync(
            tutorId, originalClassSessionId, scheduledStartUtc, scheduledEndUtc);
        if (hasTutorConflict)
            throw new ClassSessionException(ClassSessionErrorCodes.InvalidClassSessionStatus, "Gia sư đã có lịch trong khung giờ học bù", 409);

        // Cùng giới hạn chuỗi với buổi phụ do ngắt kết nối / buổi học lại do hoà giải dispute
        // (DisputeRelearnPolicy.MaxRelearnSessionsPerChain) — đếm theo TỔNG số buổi trong chuỗi vì
        // ClassSessionRecordingChainHelper (nguồn hiển thị tab "Buổi N" ở trang xem lại video) đi
        // theo Originalsessionid bất kể Iscontinuation/Isdisputerelearn/Ismakeup, nên nếu không chặn
        // ở đây thì đường học-bù-no-show có thể kéo dài chuỗi vượt quá giới hạn mà 2 đường kia tuân thủ.
        var existingSessionCount = await DisputeRelearnPolicy.CountSessionsInChainAsync(_context, originalClassSessionId);
        if (existingSessionCount >= DisputeRelearnPolicy.MaxRelearnSessionsPerChain)
            throw new ClassSessionException(
                ClassSessionErrorCodes.SessionChainLimitReached,
                $"Chuỗi buổi học này đã có {DisputeRelearnPolicy.MaxRelearnSessionsPerChain} buổi — không thể tạo thêm buổi bù nữa, " +
                "vui lòng chọn \"Hoàn tiền & hủy buổi\" hoặc \"Hủy khóa học và hoàn tiền\".",
                409);

        var makeupClassSession = new ClassSession
        {
            Bookingid = originalClassSession.Bookingid,
            Tutorid = tutorId,
            Studentid = originalClassSession.Studentid,
            Scheduledstart = scheduledStartUtc,
            Scheduledend = scheduledEndUtc,
            Lessonprice = 0,
            Status = Scheduled,
            Ismakeup = true,
            Originalsessionid = originalClassSessionId,
            Createdat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow
        };

        _context.ClassSessions.Add(makeupClassSession);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Created makeup classSession {MakeupId} for original {OriginalId}", makeupClassSession.Classsessionid, originalClassSessionId);
        return MapToClassSessionDetailResponse(makeupClassSession);
    }
}
