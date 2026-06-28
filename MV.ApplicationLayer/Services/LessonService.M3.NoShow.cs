using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MV.ApplicationLayer.Helpers;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.Entities;
using MV.DomainLayer.Exceptions;
using MV.DomainLayer.Helpers;
using static MV.DomainLayer.Constants.LessonStatus;

namespace MV.ApplicationLayer.Services;

public partial class LessonService
{
    // ── M3-T7: No-show Handling ───────────────────────────────────────────────

    public async Task<LessonDetailResponse> ReportTutorNoShowAsync(int lessonId, string parentId)
    {
        var studentIds = await _context.Studentprofiles
            .Where(s => s.Parentid == parentId)
            .Select(s => s.Studentid)
            .ToListAsync();

        var lesson = await _context.Lessons
            .Include(l => l.Booking)
            .FirstOrDefaultAsync(l => l.Lessonid == lessonId && studentIds.Contains(l.Studentid!))
            ?? throw new LessonException(LessonErrorCodes.LessonNotFound, "Không tìm thấy buổi học hoặc bạn không có quyền truy cập", 404);

        var now = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;
        if ((now - lesson.Scheduledstart).TotalMinutes < 15)
            throw new LessonException(LessonErrorCodes.TooEarlyToReportNoShow, "Chỉ có thể báo cáo vắng mặt sau 15 phút kể từ giờ bắt đầu", 400);

        if (lesson.Status != Scheduled)
            throw new LessonException(LessonErrorCodes.InvalidLessonStatus, "Buổi học không ở trạng thái đã lên lịch", 400);

        lesson.Status = NoShow;
        lesson.Istutorpresent = false;

        // Auto-create dispute record to track no-show
        var dispute = new Dispute
        {
            Lessonid = lessonId,
            Bookingid = lesson.Bookingid,
            Createdby = parentId,
            Disputetype = DisputeTypes.NoShow,
            Reason = "Tutor no-show: Gia sư không có mặt sau 15 phút",
            Status = DisputeStatus.Pending,
            Createdat = now
        };
        _context.Disputes.Add(dispute);

        await _context.SaveChangesAsync();

        // Notify tutor about the no-show report
        if (!string.IsNullOrEmpty(lesson.Tutorid))
        {
            await _notificationService.CreateNotificationAsync(new NotificationRequest
            {
                Userid = lesson.Tutorid,
                Title = "Báo cáo vắng mặt",
                Message = $"Phụ huynh đã báo cáo bạn vắng mặt cho buổi học #{lessonId}."
            });
        }

        _logger.LogInformation("Parent {ParentId} reported tutor no-show for lesson {LessonId}, dispute {DisputeId} created", parentId, lessonId, dispute.Disputeid);
        return MapToLessonDetailResponse(lesson);
    }

    public async Task<NoShowActionResultResponse> ProcessNoShowActionAsync(int lessonId, string parentId, NoShowActionRequest request)
    {
        // Pre-tx: ownership + fast-fail status check (stale read OK here)
        var studentIds = await _context.Studentprofiles
            .Where(s => s.Parentid == parentId)
            .Select(s => s.Studentid)
            .ToListAsync();

        var lesson = await _context.Lessons
            .Include(l => l.Booking)
            .FirstOrDefaultAsync(l => l.Lessonid == lessonId && studentIds.Contains(l.Studentid!))
            ?? throw new LessonException(LessonErrorCodes.LessonNotFound, "Không tìm thấy buổi học hoặc bạn không có quyền truy cập", 404);

        if (lesson.Status != NoShow)
            throw new LessonException(LessonErrorCodes.InvalidLessonStatus, "Buổi học không ở trạng thái vắng mặt", 400);

        var result = new NoShowActionResultResponse { LessonId = lessonId, ActionType = request.ActionType, Success = true };

        await using var tx = await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        try
        {
            // Lock wallets FIRST to serialize concurrent calls (FOR UPDATE row lock)
            var tutorWallet = await _context.Wallets
                .FromSqlRaw(SqlQueries.LockWalletByUserId, lesson.Tutorid)
                .FirstOrDefaultAsync();
            var parentWallet = await _context.Wallets
                .FromSqlRaw(SqlQueries.LockWalletByUserId, parentId)
                .FirstOrDefaultAsync();

            // Fresh DB read inside tx — AsNoTracking bypasses EF identity map, picks up concurrent commits
            var freshState = await _context.Lessons
                .AsNoTracking()
                .Where(l => l.Lessonid == lessonId)
                .Select(l => new { l.Issettled, l.Status })
                .FirstOrDefaultAsync();

            if (freshState?.Issettled == true)
                throw new LessonException(LessonErrorCodes.LessonAlreadyConfirmed, "Buổi học này đã được xử lý rồi", 400);
            if (freshState?.Status != NoShow)
                throw new LessonException(LessonErrorCodes.InvalidLessonStatus, "Buổi học không còn ở trạng thái no-show", 400);

            var booking = lesson.Booking
                ?? throw new InvalidOperationException($"Booking for lesson {lessonId} not found");

            var parentRefundPerSession = LessonRefundCalculator.ParentRefundPerSession(booking);
            var tutorEscrowPerSession = LessonRefundCalculator.TutorEscrowPerSession(booking);
            var now = TimeZoneHelper.UtcNow;

            lesson.Noshowaction = request.ActionType;

            switch (request.ActionType)
            {
                case NoShowActionTypes.FreeSession:
                    lesson.Status = Cancelled;
                    lesson.Issettled = true;

                    if (tutorWallet != null && tutorEscrowPerSession > 0)
                    {
                        tutorWallet.Frozenbalance = Math.Max(0, (tutorWallet.Frozenbalance ?? 0) - tutorEscrowPerSession);
                        tutorWallet.Lastupdated = now;
                        _context.Wallettransactions.Add(new Wallettransaction
                        {
                            Walletid = tutorWallet.Walletid,
                            Amount = tutorEscrowPerSession,
                            Transactiontype = TransactionType.EscrowRelease,
                            Referencetable = ReferenceTable.Booking,
                            Referenceid = lesson.Bookingid,
                            Description = $"Giải phóng escrow no-show lesson #{lessonId}",
                            Createdat = now
                        });
                    }

                    if (parentWallet != null && parentRefundPerSession > 0)
                    {
                        parentWallet.Balance += parentRefundPerSession;
                        parentWallet.Lastupdated = now;
                        _context.Wallettransactions.Add(new Wallettransaction
                        {
                            Walletid = parentWallet.Walletid,
                            Amount = parentRefundPerSession,
                            Transactiontype = TransactionType.Refund,
                            Referencetable = ReferenceTable.Booking,
                            Referenceid = lesson.Bookingid,
                            Description = $"Hoàn tiền no-show lesson #{lessonId}",
                            Createdat = now
                        });
                    }

                    result.AmountRefunded = parentRefundPerSession;
                    result.Message = "Buổi học đã được hủy và hoàn tiền 100%";
                    break;

                case NoShowActionTypes.Makeup:
                    if (!request.NewScheduledStart.HasValue)
                        throw new LessonException(LessonErrorCodes.MakeupTimeRequired, "Vui lòng cung cấp thời gian học bù mới", 400);
                    var makeupLesson = await CreateMakeupLessonAsync(lessonId, request.NewScheduledStart.Value, lesson.Tutorid!);
                    // Mark original resolved so a repeated no-show-action is blocked by the idempotency guard.
                    // Escrow of the original stays frozen until the makeup is settled (known issue #10).
                    lesson.Issettled = true;
                    result.MakeupLessonId = makeupLesson.LessonId;
                    result.Message = $"Buổi học bù đã được tạo vào {request.NewScheduledStart:dd/MM/yyyy HH:mm}";
                    break;

                case NoShowActionTypes.ChangeTutor:
                    lesson.Status = Cancelled;
                    lesson.Issettled = true;

                    var remaining = booking.Sessionsremaining ?? 0;

                    // Clamp parent refund against what was actually paid minus any prior booking refunds
                    decimal totalPaidByParent = booking.Remainingpaidat.HasValue
                        ? (booking.Finalprice ?? 0)
                        : (booking.Depositpaidat.HasValue ? (booking.Depositamount ?? 0) : 0m);
                    var totalAlreadyRefunded = await _context.Wallettransactions
                        .Where(wt => wt.Referencetable == ReferenceTable.Booking
                                  && wt.Referenceid == lesson.Bookingid
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
                            Amount = tutorEscrowRelease,
                            Transactiontype = TransactionType.EscrowRelease,
                            Referencetable = ReferenceTable.Booking,
                            Referenceid = lesson.Bookingid,
                            Description = $"Giải phóng escrow no-show change tutor - booking #{lesson.Bookingid} ({remaining} buổi còn lại)",
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
                            Referenceid = lesson.Bookingid,
                            Description = $"Hoàn tiền no-show change tutor - booking #{lesson.Bookingid} ({remaining} buổi còn lại)",
                            Createdat = now
                        });
                    }

                    // Cancel all other Scheduled/Reserved lessons in this booking
                    var futureLessons = await _context.Lessons
                        .Where(l => l.Bookingid == lesson.Bookingid
                                 && l.Lessonid != lessonId
                                 && (l.Status == Scheduled || l.Status == LessonStatus.Reserved))
                        .ToListAsync();
                    foreach (var fl in futureLessons)
                        fl.Status = Cancelled;

                    booking.Status = BookingStatus.CancelledNoshow;
                    booking.Sessionsremaining = 0;

                    result.AmountRefunded = parentTotalRefund;
                    result.Message = "Đã hủy booking và hoàn tiền các buổi còn lại";
                    break;
            }

            _context.Userwarnings.Add(new Userwarning
            {
                Userid = lesson.Tutorid,
                Warninglevel = 1,
                Reason = "Tutor no-show for lesson",
                Relatedbookingid = lesson.Bookingid,
                Createdat = now
            });
            result.WarningCreated = true;

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
            if (!string.IsNullOrEmpty(lesson.Tutorid))
            {
                await _notificationService.CreateNotificationAsync(new NotificationRequest
                {
                    Userid = lesson.Tutorid,
                    Title = "Xử lý vắng mặt",
                    Message = $"Phụ huynh đã chọn '{request.ActionType}' cho buổi học #{lessonId} bị vắng mặt."
                });
            }
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Failed to notify tutor for no-show action lesson {LessonId}", lessonId); }

        _logger.LogInformation("NoShow action {ActionType} processed for lesson {LessonId} by parent {ParentId}",
            request.ActionType, lessonId, parentId);
        return result;
    }

    public async Task<LessonDetailResponse> CreateMakeupLessonAsync(int originalLessonId, DateTime newScheduledStart, string tutorId)
    {
        var originalLesson = await _context.Lessons
            .Include(l => l.Booking)
            .FirstOrDefaultAsync(l => l.Lessonid == originalLessonId && l.Tutorid == tutorId)
            ?? throw new LessonException(LessonErrorCodes.LessonNotFound, "Không tìm thấy buổi học gốc", 404);

        var duration = originalLesson.Scheduledend - originalLesson.Scheduledstart;

        // Normalize timezone: nếu frontend gửi UTC thì convert sang UTC, nếu Unspecified thì coi như user time
        var scheduledStartUtc = newScheduledStart.Kind == DateTimeKind.Utc 
            ? newScheduledStart 
            : DateTime.SpecifyKind(newScheduledStart, DateTimeKind.Utc);

        var makeupLesson = new Lesson
        {
            Bookingid = originalLesson.Bookingid,
            Tutorid = tutorId,
            Studentid = originalLesson.Studentid,
            Scheduledstart = scheduledStartUtc,
            Scheduledend = scheduledStartUtc.Add(duration),
            Lessonprice = 0,
            Status = Scheduled,
            Ismakeup = true,
            Originalessonid = originalLessonId,
            Createdat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow
        };

        _context.Lessons.Add(makeupLesson);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Created makeup lesson {MakeupId} for original {OriginalId}", makeupLesson.Lessonid, originalLessonId);
        return MapToLessonDetailResponse(makeupLesson);
    }
}
