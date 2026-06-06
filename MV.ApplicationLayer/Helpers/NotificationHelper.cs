using MV.DomainLayer.Entities;
using MV.DomainLayer.Constants;
using MV.DomainLayer.Helpers;

namespace MV.ApplicationLayer.Helpers;

public static class NotificationHelper
{
    public static Notification CreateBookingNotification(string userId, string title, string message, int bookingId)
    {
        return new Notification
        {
            Userid = userId,
            Title = title,
            Message = message,
            Type = NotificationType.BookingNew,
            Referenceid = bookingId.ToString(),
            Isread = false,
            Createdat = VietnamTimeHelper.UtcNow
        };
    }

    public static Notification CreatePaymentNotification(string userId, string title, string message, int bookingId)
    {
        return new Notification
        {
            Userid = userId,
            Title = title,
            Message = message,
            Type = NotificationType.PaymentSuccess,
            Referenceid = bookingId.ToString(),
            Isread = false,
            Createdat = VietnamTimeHelper.UtcNow
        };
    }
}
