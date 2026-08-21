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

    public async Task<ClassSessionDetailResponse> ReportTutorNoShowAsync(int classSessionId, string userId, string role, ReportNoShowRequest? request = null)
    {
        var studentIds = role == UserRole.Parent
            ? await _context.Studentprofiles.Where(s => s.Parentid == userId).Select(s => s.Studentid).ToListAsync()
            : await _context.Studentprofiles.Where(s => s.Studentid == userId || s.Linkeduserid == userId).Select(s => s.Studentid).ToListAsync();

        var classSession = await _context.ClassSessions
            .Include(l => l.Booking)
            .FirstOrDefaultAsync(l => l.Classsessionid == classSessionId && studentIds.Contains(l.Studentid!))
            ?? throw new ClassSessionException(ClassSessionErrorCodes.ClassSessionNotFound, "Không tìm thấy buổi học hoặc bạn không có quyền truy cập", 404);

        if (role == UserRole.Student)
        {
            var studentProfile = await _context.Studentprofiles.FirstOrDefaultAsync(s => s.Studentid == userId || s.Linkeduserid == userId);
            if (studentProfile != null && studentProfile.Parentid != null)
                throw new ClassSessionException(BookingErrorCodes.StudentManagedByParent, "Tài khoản học sinh do phụ huynh quản lý không thể tự báo cáo vắng mặt", 403);
        }

        var now = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;

        if (classSession.Status != Scheduled)
            throw new ClassSessionException(ClassSessionErrorCodes.InvalidClassSessionStatus, "Buổi học không ở trạng thái đã lên lịch", 400);
        if (DisputeSettlementPolicy.IsTerminalBooking(classSession.Booking?.Status))
            throw new ClassSessionException(ClassSessionErrorCodes.InvalidClassSessionStatus, "Booking đã kết thúc, không thể tạo báo cáo mới", 400);

        classSession.Status = NoShow;
        classSession.Istutorpresent = false;

        // Reported time is advisory context for admin only — not a gate (no more 15-minute
        // requirement, per product decision to let the reporter flag no-show any time the
        // session is still Scheduled).
        var reportedAt = request?.ReportedAt ?? now;
        var reasonText = !string.IsNullOrWhiteSpace(request?.Reason)
            ? $"Tutor no-show lúc {reportedAt:dd/MM/yyyy HH:mm}: {request!.Reason}"
            : $"Tutor no-show: Gia sư không có mặt lúc {reportedAt:dd/MM/yyyy HH:mm}";

        var uploadedEvidence = new List<string>();
        var evidenceFolder = $"dispute-evidence-{classSessionId}";
        Dispute dispute;

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
        }
        catch
        {
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

            var classSession = await _context.ClassSessions
                .FromSqlRaw(SqlQueries.LockClassSessionById, classSessionId)
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

        var hasTutorConflict = await _context.ClassSessions.AnyAsync(l =>
            l.Tutorid == tutorId
            && l.Classsessionid != originalClassSessionId
            && l.Status != Cancelled
            && l.Status != CancelledNoshow
            && l.Scheduledstart < scheduledEndUtc
            && l.Scheduledend > scheduledStartUtc);
        if (hasTutorConflict)
            throw new ClassSessionException(ClassSessionErrorCodes.InvalidClassSessionStatus, "Gia sư đã có lịch trong khung giờ học bù", 409);

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
