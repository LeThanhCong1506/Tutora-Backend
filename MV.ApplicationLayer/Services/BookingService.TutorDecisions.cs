using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MV.ApplicationLayer.Helpers;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.Entities;
using MV.DomainLayer.Exceptions;
using MV.DomainLayer.Helpers;

namespace MV.ApplicationLayer.Services;

public partial class BookingService
{
    // ─── Tutor Accept / Decline ───────────────────────────────────────────

    public async Task<TutorDecisionResponse> AcceptBookingAsync(string tutorId, int bookingId)
    {
        var booking = await bookingRepo.FindWithRelationsAsync(bookingId);

        if (booking == null)
            throw new BookingException(BookingErrorCodes.BookingNotFound, "Không tìm thấy booking", 404);

        if (booking.Tutorid != tutorId)
            throw new BookingException(BookingErrorCodes.NotBookingOwner, "Bạn không phải gia sư của booking này", 403);

        if (booking.Status == BookingStatus.DepositPaid || booking.Status == BookingStatus.Paid)
        {
            var existingChannelId = await chatService.GetOrCreateChannelAsync(booking.Parentid!, tutorId);
            return new TutorDecisionResponse
            {
                Booking = MapToResponse(booking, booking.Student, booking.Tutor, booking.Tutorsubjectgradeprice?.Subject),
                ChannelId = existingChannelId
            };
        }

        if (booking.Status != BookingStatus.PendingTutor)
            throw new BookingException(BookingErrorCodes.InvalidBookingStatus, $"Không thể chấp nhận booking ở trạng thái '{booking.Status}'", 400);

        if (booking.Depositpaidat == null)
            throw new BookingException(BookingErrorCodes.InvalidBookingStatus, "Booking chưa được thanh toán cọc", 409);

        // Không tính lại Depositamount — parent đã trả thực tế theo số đó rồi.
        // Chỉ bổ sung Remainingamount nếu còn null (safety net cho data cũ).
        EnsureRemainingAmountCalculated(booking);

        // Booking 1 buổi (Remaining = 0): sau khi trả deposit là xong → Paid.
        // Booking N buổi (Remaining > 0): chờ parent trả phần còn lại → DepositPaid.
        booking.Status = (booking.Remainingamount ?? 0) == 0 || booking.Remainingpaidat != null
            ? BookingStatus.Paid
            : BookingStatus.DepositPaid;
        booking.Updatedat = TimeZoneHelper.UtcNow;
        booking.Responsedeadline = null;

        var scheduledLessons = booking.Lessons
            .Where(lesson => lesson.Status == LessonStatus.Reserved)
            .OrderBy(lesson => lesson.Scheduledstart)
            .ToList();

        foreach (var lesson in scheduledLessons)
        {
            lesson.Status = LessonStatus.Scheduled;
            lesson.Meetinglink ??= lesson.Lessonid.ToString();
        }

        // Save booking changes FIRST — before any chat/notification operations that share
        // the same DbContext, to avoid DbUpdateException from concurrent entity tracking.
        await bookingRepo.SaveChangesAsync();

        var channelId = 0;
        try
        {
            channelId = await chatService.GetOrCreateChannelAsync(booking.Parentid!, tutorId);
            await chatService.SendMessageAsync(tutorId, channelId, new ChatMessageCreateRequest
            {
                Content = "✅ Gia sư đã chấp nhận yêu cầu đặt lịch",
                MessageType = ChatMessageType.BookingAccepted,
                Metadata = new { bookingId, status = booking.Status }
            });

            if (scheduledLessons.Count > 0)
            {
                await chatService.SendMeetLinksAsync(bookingId, scheduledLessons.Select((lesson, index) => new LessonMiniResponse
                {
                    LessonId = lesson.Lessonid,
                    BookingId = bookingId,
                    SessionIndex = index + 1,
                    ScheduledStart = lesson.Scheduledstart,
                    ScheduledEnd = lesson.Scheduledend,
                    MeetingLink = lesson.Meetinglink ?? "",
                    Status = lesson.Status ?? LessonStatus.Scheduled
                }).ToList());
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send chat message for booking {BookingId}", bookingId);
        }

        try
        {
            await notificationService.CreateNotificationAsync(new NotificationRequest
            {
                Userid = booking.Parentid!,
                Title = "Gia sư đã chấp nhận lịch học",
                Message = $"Gia sư đã chấp nhận yêu cầu đặt lịch #{bookingId}. Buổi học đã được lên lịch.",
                Type = NotificationType.BookingAccepted,
                Referenceid = bookingId.ToString()
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send accept notification for booking {BookingId}", bookingId);
        }

        return new TutorDecisionResponse
        {
            Booking = MapToResponse(booking, booking.Student, booking.Tutor, booking.Tutorsubjectgradeprice?.Subject),
            ChannelId = channelId
        };
    }

    public async Task<BookingResponse> DeclineBookingAsync(string tutorId, int bookingId, string? reason)
    {
        var booking = await bookingRepo.FindWithRelationsAsync(bookingId);

        if (booking == null)
            throw new BookingException(BookingErrorCodes.BookingNotFound, "Không tìm thấy booking", 404);

        if (booking.Tutorid != tutorId)
            throw new BookingException(BookingErrorCodes.NotBookingOwner, "Bạn không phải gia sư của booking này", 403);

        if (booking.Status != BookingStatus.PendingTutor)
            throw new BookingException(BookingErrorCodes.InvalidBookingStatus, $"Không thể từ chối booking ở trạng thái '{booking.Status}'", 400);

        await using var tx = await context.Database.BeginTransactionAsync();
        try
        {
            booking.Status = BookingStatus.Cancelled;
            booking.Cancellationreason = reason;
            booking.Cancelledby = tutorId;
            booking.Cancelledat = TimeZoneHelper.UtcNow;
            booking.Updatedat = TimeZoneHelper.UtcNow;

            foreach (var lesson in booking.Lessons.Where(l => l.Status == LessonStatus.Reserved))
                lesson.Status = LessonStatus.Cancelled;

            await RefundPaidBookingAsync(booking, "Hoàn tiền do gia sư từ chối booking");
            await bookingRepo.SaveChangesAsync();
            await tx.CommitAsync();
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }

        try
        {
            await notificationService.CreateNotificationAsync(new NotificationRequest
            {
                Userid = booking.Parentid!,
                Title = "Gia sư đã từ chối lịch học",
                Message = $"Gia sư đã từ chối yêu cầu đặt lịch #{bookingId}. Tiền cọc đã được hoàn vào ví của bạn.",
                Type = NotificationType.BookingDeclined,
                Referenceid = bookingId.ToString()
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send decline notification for booking {BookingId}", bookingId);
        }

        return MapToResponse(booking, booking.Student, booking.Tutor, booking.Tutorsubjectgradeprice?.Subject);
    }

    private async Task RefundPaidBookingAsync(Booking booking, string description)
    {
        if (booking.Depositpaidat == null)
        {
            booking.Refundstatus = RefundStatus.NoRefund;
            return;
        }

        var refundAmount = booking.Paymentstatus == PaymentStatus.Escrowed || booking.Remainingpaidat != null
            ? booking.Finalprice ?? booking.Totalamount ?? 0
            : booking.Depositamount ?? 0;

        if (refundAmount <= 0 || string.IsNullOrWhiteSpace(booking.Parentid))
        {
            booking.Refundstatus = RefundStatus.NoRefund;
            return;
        }

        var parentWallet = await context.Wallets
            .FromSqlRaw(SqlQueries.LockWalletByUserId, booking.Parentid)
            .FirstOrDefaultAsync();

        if (parentWallet != null)
        {
            parentWallet.Balance = (parentWallet.Balance ?? 0) + refundAmount;
            parentWallet.Lastupdated = TimeZoneHelper.UtcNow;

            context.Wallettransactions.Add(new Wallettransaction
            {
                Wallet = parentWallet,
                Amount = refundAmount,
                Transactiontype = TransactionType.Refund,
                Referencetable = ReferenceTable.Booking,
                Referenceid = booking.Bookingid,
                Description = description,
                Createdat = TimeZoneHelper.UtcNow
            });
        }

        if (!string.IsNullOrWhiteSpace(booking.Tutorid))
        {
            var tutorWallet = await context.Wallets
                .FromSqlRaw(SqlQueries.LockWalletByUserId, booking.Tutorid)
                .FirstOrDefaultAsync();

            if (tutorWallet != null)
            {
                var tutorEscrowAmount = booking.Paymentstatus == PaymentStatus.Escrowed || booking.Remainingpaidat != null
                    ? booking.Tutorfee ?? 0
                    : Math.Round((booking.Tutorfee ?? 0) / Math.Max(booking.Totalsessions ?? 1, 1), 2);

                tutorWallet.Frozenbalance = Math.Max(0, (tutorWallet.Frozenbalance ?? 0) - tutorEscrowAmount);
                tutorWallet.Lastupdated = TimeZoneHelper.UtcNow;

                context.Wallettransactions.Add(new Wallettransaction
                {
                    Wallet = tutorWallet,
                    Amount = -tutorEscrowAmount,
                    Transactiontype = TransactionType.EscrowRelease,
                    Referencetable = ReferenceTable.Booking,
                    Referenceid = booking.Bookingid,
                    Description = $"{description} - release escrow",
                    Createdat = TimeZoneHelper.UtcNow
                });
            }
        }

        booking.Refundamount = refundAmount;
        booking.Refundstatus = RefundStatus.Refunded;
    }

    /// <summary>
    /// Bổ sung Remainingamount nếu còn null — không overwrite Depositamount đã được confirm thanh toán.
    /// </summary>
    private static void EnsureRemainingAmountCalculated(Booking booking)
    {
        if (booking.Remainingamount != null) return;

        var finalPrice = booking.Finalprice ?? 0;
        var deposit = booking.Depositamount ?? 0;

        // Depositamount chưa có — tính lại cả hai từ đầu
        if (deposit == 0)
        {
            var sessions = booking.Totalsessions ?? 1;
            var (d, r) = BookingFeeCalculator.CalculatePaymentPhases(finalPrice, sessions);
            booking.Depositamount = d;
            booking.Remainingamount = r;
        }
        else
        {
            booking.Remainingamount = finalPrice - deposit;
        }
    }
}
