using Microsoft.Extensions.Logging;
using MV.ApplicationLayer.Helpers;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;
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

        if (booking.Status != BookingStatus.PendingTutor && booking.Status != BookingStatus.Accepted)
            throw new BookingException(BookingErrorCodes.InvalidBookingStatus, $"Không thể chấp nhận booking ở trạng thái '{booking.Status}'", 400);

        if (booking.Status == BookingStatus.Accepted)
        {
            var existingChannelId = await chatService.GetOrCreateChannelAsync(booking.Parentid!, tutorId);
            return new TutorDecisionResponse
            {
                Booking = MapToResponse(booking, booking.Student, booking.Tutor, booking.Tutorsubjectgradeprice?.Subject),
                ChannelId = existingChannelId
            };
        }

        booking.Status = BookingStatus.Accepted;
        booking.Updatedat = TimeZoneHelper.UtcNow;
        booking.Paymentdueat = TimeZoneHelper.UtcNow.AddHours(24);
        booking.Depositamount = Math.Ceiling((booking.Finalprice ?? 0) * 0.5m);
        booking.Remainingamount = (booking.Finalprice ?? 0) - booking.Depositamount.Value;

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
                Metadata = new { bookingId, status = BookingStatus.Accepted }
            });
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
                Message = $"Gia sư đã chấp nhận yêu cầu đặt lịch #{bookingId}"
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

        booking.Status = BookingStatus.Cancelled;
        booking.Cancellationreason = reason;
        booking.Cancelledby = tutorId;
        booking.Cancelledat = TimeZoneHelper.UtcNow;
        booking.Updatedat = TimeZoneHelper.UtcNow;

        foreach (var lesson in booking.Lessons.Where(l => l.Status == LessonStatus.Reserved))
            lesson.Status = LessonStatus.Cancelled;

        await bookingRepo.SaveChangesAsync();

        try
        {
            await notificationService.CreateNotificationAsync(new NotificationRequest
            {
                Userid = booking.Parentid!,
                Title = "Gia sư đã từ chối lịch học",
                Message = $"Gia sư đã từ chối yêu cầu đặt lịch #{bookingId}"
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send decline notification for booking {BookingId}", bookingId);
        }

        return MapToResponse(booking, booking.Student, booking.Tutor, booking.Tutorsubjectgradeprice?.Subject);
    }
}
