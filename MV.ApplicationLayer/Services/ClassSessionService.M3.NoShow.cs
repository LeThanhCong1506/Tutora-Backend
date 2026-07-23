using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MV.ApplicationLayer.Helpers;
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

        classSession.Status = NoShow;
        classSession.Istutorpresent = false;

        // Reported time is advisory context for admin only — not a gate (no more 15-minute
        // requirement, per product decision to let the reporter flag no-show any time the
        // session is still Scheduled).
        var reportedAt = request?.ReportedAt ?? now;
        var reasonText = !string.IsNullOrWhiteSpace(request?.Reason)
            ? $"Tutor no-show lúc {reportedAt:dd/MM/yyyy HH:mm}: {request!.Reason}"
            : $"Tutor no-show: Gia sư không có mặt lúc {reportedAt:dd/MM/yyyy HH:mm}";

        // Auto-create dispute record to track no-show
        var dispute = new Dispute
        {
            Classsessionid = classSessionId,
            Bookingid = classSession.Bookingid,
            Createdby = userId,
            Disputetype = DisputeTypes.NoShow,
            Reason = reasonText,
            Status = DisputeStatus.Pending,
            Createdat = now
        };
        _context.Disputes.Add(dispute);

        await _context.SaveChangesAsync();

        // Notify tutor about the no-show report
        if (!string.IsNullOrEmpty(classSession.Tutorid))
        {
            await _notificationService.CreateNotificationAsync(new NotificationRequest
            {
                Userid = classSession.Tutorid,
                Title = "Báo cáo vắng mặt",
                Message = $"Bạn đã bị báo cáo vắng mặt cho buổi học #{classSessionId}."
            });
        }

        _logger.LogInformation("User {UserId} ({Role}) reported tutor no-show for classSession {ClassSessionId}, dispute {DisputeId} created", userId, role, classSessionId, dispute.Disputeid);
        return MapToClassSessionDetailResponse(classSession);
    }

    public async Task<NoShowActionResultResponse> ProcessNoShowActionAsync(int classSessionId, string userId, string role, NoShowActionRequest request)
    {
        // Pre-tx: ownership + fast-fail status check (stale read OK here)
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
                throw new ClassSessionException(BookingErrorCodes.StudentManagedByParent, "Tài khoản học sinh do phụ huynh quản lý không thể tự xử lý vắng mặt", 403);
        }

        if (classSession.Status != NoShow)
            throw new ClassSessionException(ClassSessionErrorCodes.InvalidClassSessionStatus, "Buổi học không ở trạng thái vắng mặt", 400);

        var result = new NoShowActionResultResponse { ClassSessionId = classSessionId, ActionType = request.ActionType, Success = true };

        await using var tx = await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        try
        {
            // Lock wallets FIRST to serialize concurrent calls (FOR UPDATE row lock)
            var tutorWallet = await _context.Wallets
                .FromSqlRaw(SqlQueries.LockWalletByUserId, classSession.Tutorid)
                .FirstOrDefaultAsync();
            var parentWallet = await _context.Wallets
                .FromSqlRaw(SqlQueries.LockWalletByUserId, userId)
                .FirstOrDefaultAsync();

            // Fresh DB read inside tx — AsNoTracking bypasses EF identity map, picks up concurrent commits
            var freshState = await _context.ClassSessions
                .AsNoTracking()
                .Where(l => l.Classsessionid == classSessionId)
                .Select(l => new { l.Issettled, l.Status })
                .FirstOrDefaultAsync();

            if (freshState?.Issettled == true)
                throw new ClassSessionException(ClassSessionErrorCodes.ClassSessionAlreadyConfirmed, "Buổi học này đã được xử lý rồi", 400);
            if (freshState?.Status != NoShow)
                throw new ClassSessionException(ClassSessionErrorCodes.InvalidClassSessionStatus, "Buổi học không còn ở trạng thái no-show", 400);

            var booking = classSession.Booking
                ?? throw new InvalidOperationException($"Booking for classSession {classSessionId} not found");

            var parentRefundPerSession = LessonRefundCalculator.ParentRefundPerSession(booking);
            var tutorEscrowPerSession = LessonRefundCalculator.TutorEscrowPerSession(booking);
            var now = TimeZoneHelper.UtcNow;

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

            // Close out the auto-created no-show dispute so it doesn't sit "pending" forever in the
            // admin queue after the parent has already resolved it themselves. Guarded against
            // Resolved/Closed in case an admin somehow already acted on it concurrently.
            var noShowDispute = await _context.Disputes
                .FirstOrDefaultAsync(d => d.Classsessionid == classSessionId
                    && d.Disputetype == DisputeTypes.NoShow
                    && d.Status != DisputeStatus.Resolved
                    && d.Status != DisputeStatus.Closed);
            if (noShowDispute != null)
            {
                var disputeRefundPercentage = request.ActionType == NoShowActionTypes.Makeup ? 0 : 100;
                noShowDispute.Status = DisputeStatus.Resolved;
                noShowDispute.Resolvedat = now;
                noShowDispute.Resolvedby = userId;
                noShowDispute.Resolutionnote = $"Người dùng tự xử lý ({request.ActionType}): {result.Message}";
                noShowDispute.Refundpercentage = disputeRefundPercentage;
                noShowDispute.Refundamount = result.AmountRefunded;
                noShowDispute.Refundissued = result.AmountRefunded > 0;
            }

            // Makeup reschedules the session with the tutor's agreement — not a fault finding,
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
            if (!string.IsNullOrEmpty(classSession.Tutorid))
            {
                await _notificationService.CreateNotificationAsync(new NotificationRequest
                {
                    Userid = classSession.Tutorid,
                    Title = "Xử lý vắng mặt",
                    Message = $"'{request.ActionType}' đã được chọn cho buổi học #{classSessionId} bị vắng mặt."
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

        // Normalize timezone: nếu frontend gửi UTC thì convert sang UTC, nếu Unspecified thì coi như user time
        var scheduledStartUtc = newScheduledStart.Kind == DateTimeKind.Utc
            ? newScheduledStart
            : DateTime.SpecifyKind(newScheduledStart, DateTimeKind.Utc);

        var makeupClassSession = new ClassSession
        {
            Bookingid = originalClassSession.Bookingid,
            Tutorid = tutorId,
            Studentid = originalClassSession.Studentid,
            Scheduledstart = scheduledStartUtc,
            Scheduledend = scheduledStartUtc.Add(duration),
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
