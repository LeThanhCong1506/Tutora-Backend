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

    public async Task<ClassSessionDetailResponse> ReportTutorNoShowAsync(int classSessionId, string parentId)
    {
        var studentIds = await _context.Studentprofiles
            .Where(s => s.Parentid == parentId)
            .Select(s => s.Studentid)
            .ToListAsync();

        var classSession = await _context.ClassSessions
            .Include(l => l.Booking)
            .FirstOrDefaultAsync(l => l.Classsessionid == classSessionId && studentIds.Contains(l.Studentid!))
            ?? throw new ClassSessionException(ClassSessionErrorCodes.ClassSessionNotFound, "Không tìm thấy buổi học hoặc bạn không có quyền truy cập", 404);

        var now = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;
        if ((now - classSession.Scheduledstart).TotalMinutes < 15)
            throw new ClassSessionException(ClassSessionErrorCodes.TooEarlyToReportNoShow, "Chỉ có thể báo cáo vắng mặt sau 15 phút kể từ giờ bắt đầu", 400);

        if (classSession.Status != Scheduled)
            throw new ClassSessionException(ClassSessionErrorCodes.InvalidClassSessionStatus, "Buổi học không ở trạng thái đã lên lịch", 400);

        classSession.Status = NoShow;
        classSession.Istutorpresent = false;

        // Auto-create dispute record to track no-show
        var dispute = new Dispute
        {
            Classsessionid = classSessionId,
            Bookingid = classSession.Bookingid,
            Createdby = parentId,
            Disputetype = DisputeTypes.NoShow,
            Reason = "Tutor no-show: Gia sư không có mặt sau 15 phút",
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
                Message = $"Phụ huynh đã báo cáo bạn vắng mặt cho buổi học #{classSessionId}."
            });
        }

        _logger.LogInformation("Parent {ParentId} reported tutor no-show for classSession {ClassSessionId}, dispute {DisputeId} created", parentId, classSessionId, dispute.Disputeid);
        return MapToClassSessionDetailResponse(classSession);
    }

    public async Task<NoShowActionResultResponse> ProcessNoShowActionAsync(int classSessionId, string parentId, NoShowActionRequest request)
    {
        // Pre-tx: ownership + fast-fail status check (stale read OK here)
        var studentIds = await _context.Studentprofiles
            .Where(s => s.Parentid == parentId)
            .Select(s => s.Studentid)
            .ToListAsync();

        var classSession = await _context.ClassSessions
            .Include(l => l.Booking)
            .FirstOrDefaultAsync(l => l.Classsessionid == classSessionId && studentIds.Contains(l.Studentid!))
            ?? throw new ClassSessionException(ClassSessionErrorCodes.ClassSessionNotFound, "Không tìm thấy buổi học hoặc bạn không có quyền truy cập", 404);

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
                .FromSqlRaw(SqlQueries.LockWalletByUserId, parentId)
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
                    classSession.Status = Cancelled;
                    classSession.Issettled = true;

                    // Clamp parent refund against what was actually paid minus any prior booking refunds
                    // (mirrors ChangeTutor's clamp below, scoped to 1 session instead of `remaining`).
                    decimal totalPaidByParentSingle = booking.Remainingpaidat.HasValue
                        ? (booking.Finalprice ?? 0)
                        : (booking.Depositpaidat.HasValue ? (booking.Depositamount ?? 0) : 0m);
                    var totalAlreadyRefundedSingle = await _context.Wallettransactions
                        .Where(wt => wt.Referencetable == ReferenceTable.Booking
                                  && wt.Referenceid == classSession.Bookingid
                                  && wt.Transactiontype == TransactionType.Refund)
                        .SumAsync(wt => wt.Amount ?? 0);
                    var maxParentRefundSingle = Math.Max(0, totalPaidByParentSingle - totalAlreadyRefundedSingle);
                    var parentRefundClamped = Math.Round(Math.Min(parentRefundPerSession, maxParentRefundSingle), 2);

                    // Clamp tutor escrow release against actual frozen balance so the wallet-transaction
                    // ledger always matches the real balance delta (same reasoning as ChangeTutor below).
                    var tutorEscrowClamped = Math.Round(
                        Math.Min(tutorEscrowPerSession, Math.Max(0, tutorWallet?.Frozenbalance ?? 0)), 2);

                    if (tutorWallet != null && tutorEscrowClamped > 0)
                    {
                        tutorWallet.Frozenbalance = Math.Max(0, (tutorWallet.Frozenbalance ?? 0) - tutorEscrowClamped);
                        tutorWallet.Lastupdated = now;
                        _context.Wallettransactions.Add(new Wallettransaction
                        {
                            Walletid = tutorWallet.Walletid,
                            Amount = tutorEscrowClamped,
                            Transactiontype = TransactionType.EscrowReversal,
                            Referencetable = ReferenceTable.Booking,
                            Referenceid = classSession.Bookingid,
                            Description = $"Giải phóng escrow no-show classSession #{classSessionId}",
                            Createdat = now
                        });
                    }

                    if (parentWallet != null && parentRefundClamped > 0)
                    {
                        parentWallet.Balance += parentRefundClamped;
                        parentWallet.Lastupdated = now;
                        _context.Wallettransactions.Add(new Wallettransaction
                        {
                            Walletid = parentWallet.Walletid,
                            Amount = parentRefundClamped,
                            Transactiontype = TransactionType.Refund,
                            Referencetable = ReferenceTable.Booking,
                            Referenceid = classSession.Bookingid,
                            Description = $"Hoàn tiền no-show classSession #{classSessionId}",
                            Createdat = now
                        });
                    }

                    result.AmountRefunded = parentRefundClamped;
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
                            Amount = tutorEscrowRelease,
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

            _context.Userwarnings.Add(new Userwarning
            {
                Userid = classSession.Tutorid,
                Warninglevel = 1,
                Reason = "Tutor no-show for class session",
                Relatedbookingid = classSession.Bookingid,
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
            if (!string.IsNullOrEmpty(classSession.Tutorid))
            {
                await _notificationService.CreateNotificationAsync(new NotificationRequest
                {
                    Userid = classSession.Tutorid,
                    Title = "Xử lý vắng mặt",
                    Message = $"Phụ huynh đã chọn '{request.ActionType}' cho buổi học #{classSessionId} bị vắng mặt."
                });
            }
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Failed to notify tutor for no-show action classSession {ClassSessionId}", classSessionId); }

        _logger.LogInformation("NoShow action {ActionType} processed for classSession {ClassSessionId} by parent {ParentId}",
            request.ActionType, classSessionId, parentId);
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
