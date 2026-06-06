using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.Entities;
using MV.DomainLayer.Exceptions;
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

        var now = MV.DomainLayer.Helpers.VietnamTimeHelper.Now;
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

        await using var tx = await _context.Database.BeginTransactionAsync();
        try
        {
            lesson.Noshowaction = request.ActionType;

            switch (request.ActionType)
            {
                case NoShowActionTypes.FreeSession:
                    lesson.Status = Cancelled;
                    lesson.Issettled = true;

                    // Hoàn tiền 100% vào wallet parent, trừ từ tutor frozen balance
                    var refundAmount = lesson.Lessonprice ?? 0;
                    if (refundAmount > 0)
                    {
                        // Trừ frozen balance từ tutor wallet (tiền escrow nằm ở tutor)
                        var tutorWalletFree = await _context.Wallets.FirstOrDefaultAsync(w => w.Userid == lesson.Tutorid);
                        if (tutorWalletFree != null)
                        {
                            tutorWalletFree.Frozenbalance = (tutorWalletFree.Frozenbalance ?? 0) - refundAmount;
                            tutorWalletFree.Lastupdated = MV.DomainLayer.Helpers.VietnamTimeHelper.Now;
                        }

                        // Cộng tiền hoàn vào parent wallet
                        var parentWallet = await _context.Wallets.FirstOrDefaultAsync(w => w.Userid == parentId);
                        if (parentWallet != null)
                        {
                            parentWallet.Balance += refundAmount;
                            parentWallet.Lastupdated = MV.DomainLayer.Helpers.VietnamTimeHelper.Now;

                            _context.Wallettransactions.Add(new Wallettransaction
                            {
                                Walletid = parentWallet.Walletid,
                                Amount = refundAmount,
                                Transactiontype = TransactionType.Refund,
                                Description = $"Hoàn tiền no-show lesson #{lessonId}",
                                Createdat = MV.DomainLayer.Helpers.VietnamTimeHelper.Now
                            });
                        }
                    }

                    result.AmountRefunded = refundAmount;
                    result.Message = "Buổi học đã được hủy và hoàn tiền 100%";
                    break;

                case NoShowActionTypes.Makeup:
                    if (!request.NewScheduledStart.HasValue)
                        throw new LessonException(LessonErrorCodes.MakeupTimeRequired, "Vui lòng cung cấp thời gian học bù mới", 400);
                    var makeupLesson = await CreateMakeupLessonAsync(lessonId, request.NewScheduledStart.Value, lesson.Tutorid!);
                    result.MakeupLessonId = makeupLesson.LessonId;
                    result.Message = $"Buổi học bù đã được tạo vào {request.NewScheduledStart:dd/MM/yyyy HH:mm}";
                    break;

                case NoShowActionTypes.ChangeTutor:
                    lesson.Status = Cancelled;
                    if (lesson.Booking != null)
                    {
                        var remaining = lesson.Booking.Sessionsremaining ?? 0;
                        var totalRefund = remaining * (lesson.Lessonprice ?? 0);

                        // Hoàn tiền các buổi còn lại: trừ tutor frozen, cộng parent balance
                        if (totalRefund > 0)
                        {
                            // Trừ frozen balance từ tutor wallet
                            var tutorWalletChange = await _context.Wallets.FirstOrDefaultAsync(w => w.Userid == lesson.Tutorid);
                            if (tutorWalletChange != null)
                            {
                                tutorWalletChange.Frozenbalance = (tutorWalletChange.Frozenbalance ?? 0) - totalRefund;
                                tutorWalletChange.Lastupdated = MV.DomainLayer.Helpers.VietnamTimeHelper.Now;
                            }

                            // Cộng tiền hoàn vào parent wallet
                            var parentWalletForChange = await _context.Wallets.FirstOrDefaultAsync(w => w.Userid == parentId);
                            if (parentWalletForChange != null)
                            {
                                parentWalletForChange.Balance += totalRefund;
                                parentWalletForChange.Lastupdated = MV.DomainLayer.Helpers.VietnamTimeHelper.Now;

                                _context.Wallettransactions.Add(new Wallettransaction
                                {
                                    Walletid = parentWalletForChange.Walletid,
                                    Amount = totalRefund,
                                    Transactiontype = TransactionType.Refund,
                                    Description = $"Hoàn tiền change tutor - booking #{lesson.Bookingid} ({remaining} buổi còn lại)",
                                    Createdat = MV.DomainLayer.Helpers.VietnamTimeHelper.Now
                                });
                            }
                        }

                        result.AmountRefunded = totalRefund;
                        lesson.Booking.Status = BookingStatus.CancelledNoshow;
                    }
                    result.Message = "Đã hủy booking và hoàn tiền các buổi còn lại";
                    break;
            }

            var warning = new Userwarning
            {
                Userid = lesson.Tutorid,
                Warninglevel = 1,
                Reason = "Tutor no-show for lesson",
                Relatedbookingid = lesson.Bookingid,
                Createdat = MV.DomainLayer.Helpers.VietnamTimeHelper.Now
            };
            _context.Userwarnings.Add(warning);
            result.WarningCreated = true;

            await _context.SaveChangesAsync();
            await tx.CommitAsync();
            return result;
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    public async Task<LessonDetailResponse> CreateMakeupLessonAsync(int originalLessonId, DateTime newScheduledStart, string tutorId)
    {
        var originalLesson = await _context.Lessons
            .Include(l => l.Booking)
            .FirstOrDefaultAsync(l => l.Lessonid == originalLessonId && l.Tutorid == tutorId)
            ?? throw new LessonException(LessonErrorCodes.LessonNotFound, "Không tìm thấy buổi học gốc", 404);

        var duration = originalLesson.Scheduledend - originalLesson.Scheduledstart;

        var makeupLesson = new Lesson
        {
            Bookingid = originalLesson.Bookingid,
            Tutorid = tutorId,
            Studentid = originalLesson.Studentid,
            Scheduledstart = newScheduledStart,
            Scheduledend = newScheduledStart.Add(duration),
            Lessonprice = 0,
            Status = Scheduled,
            Ismakeup = true,
            Originalessonid = originalLessonId,
            Createdat = MV.DomainLayer.Helpers.VietnamTimeHelper.Now
        };

        _context.Lessons.Add(makeupLesson);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Created makeup lesson {MakeupId} for original {OriginalId}", makeupLesson.Lessonid, originalLessonId);
        return MapToLessonDetailResponse(makeupLesson);
    }
}
