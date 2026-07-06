using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
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

        await using var tx = await _context.Database.BeginTransactionAsync();
        try
        {
            classSession.Noshowaction = request.ActionType;

            switch (request.ActionType)
            {
                case NoShowActionTypes.FreeSession:
                    classSession.Status = Cancelled;
                    classSession.Issettled = true;

                    // Hoàn tiền 100% vào wallet parent, trừ từ tutor frozen balance
                    var refundAmount = classSession.Lessonprice ?? 0;
                    if (refundAmount > 0)
                    {
                        // Trừ frozen balance từ tutor wallet (tiền escrow nằm ở tutor)
                        var tutorWalletFree = await _context.Wallets.FirstOrDefaultAsync(w => w.Userid == classSession.Tutorid);
                        if (tutorWalletFree != null)
                        {
                            tutorWalletFree.Frozenbalance = (tutorWalletFree.Frozenbalance ?? 0) - refundAmount;
                            tutorWalletFree.Lastupdated = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;
                        }

                        // Cộng tiền hoàn vào parent wallet
                        var parentWallet = await _context.Wallets.FirstOrDefaultAsync(w => w.Userid == parentId);
                        if (parentWallet != null)
                        {
                            parentWallet.Balance += refundAmount;
                            parentWallet.Lastupdated = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;

                            _context.Wallettransactions.Add(new Wallettransaction
                            {
                                Walletid = parentWallet.Walletid,
                                Amount = refundAmount,
                                Transactiontype = TransactionType.Refund,
                                Description = $"Hoàn tiền no-show buổi học #{classSessionId}",
                                Createdat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow
                            });
                        }
                    }

                    result.AmountRefunded = refundAmount;
                    result.Message = "Buổi học đã được hủy và hoàn tiền 100%";
                    break;

                case NoShowActionTypes.Makeup:
                    if (!request.NewScheduledStart.HasValue)
                        throw new ClassSessionException(ClassSessionErrorCodes.MakeupTimeRequired, "Vui lòng cung cấp thời gian học bù mới", 400);
                    var makeupClassSession = await CreateMakeupClassSessionAsync(classSessionId, request.NewScheduledStart.Value, classSession.Tutorid!);
                    result.MakeupClassSessionId = makeupClassSession.ClassSessionId;
                    result.Message = $"Buổi học bù đã được tạo vào {request.NewScheduledStart:dd/MM/yyyy HH:mm}";
                    break;

                case NoShowActionTypes.ChangeTutor:
                    classSession.Status = Cancelled;
                    if (classSession.Booking != null)
                    {
                        var remaining = classSession.Booking.Sessionsremaining ?? 0;
                        var totalRefund = remaining * (classSession.Lessonprice ?? 0);

                        // Hoàn tiền các buổi còn lại: trừ tutor frozen, cộng parent balance
                        if (totalRefund > 0)
                        {
                            // Trừ frozen balance từ tutor wallet
                            var tutorWalletChange = await _context.Wallets.FirstOrDefaultAsync(w => w.Userid == classSession.Tutorid);
                            if (tutorWalletChange != null)
                            {
                                tutorWalletChange.Frozenbalance = (tutorWalletChange.Frozenbalance ?? 0) - totalRefund;
                                tutorWalletChange.Lastupdated = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;
                            }

                            // Cộng tiền hoàn vào parent wallet
                            var parentWalletForChange = await _context.Wallets.FirstOrDefaultAsync(w => w.Userid == parentId);
                            if (parentWalletForChange != null)
                            {
                                parentWalletForChange.Balance += totalRefund;
                                parentWalletForChange.Lastupdated = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;

                                _context.Wallettransactions.Add(new Wallettransaction
                                {
                                    Walletid = parentWalletForChange.Walletid,
                                    Amount = totalRefund,
                                    Transactiontype = TransactionType.Refund,
                                    Description = $"Hoàn tiền change tutor - booking #{classSession.Bookingid} ({remaining} buổi còn lại)",
                                    Createdat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow
                                });
                            }
                        }

                        result.AmountRefunded = totalRefund;
                        classSession.Booking.Status = BookingStatus.CancelledNoshow;
                    }
                    result.Message = "Đã hủy booking và hoàn tiền các buổi còn lại";
                    break;
            }

            var warning = new Userwarning
            {
                Userid = classSession.Tutorid,
                Warninglevel = 1,
                Reason = "Tutor no-show for class session",
                Relatedbookingid = classSession.Bookingid,
                Createdat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow
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
