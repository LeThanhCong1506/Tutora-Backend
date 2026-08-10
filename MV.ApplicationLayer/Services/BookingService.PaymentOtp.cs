using MV.DomainLayer.Constants;
using MV.DomainLayer.Entities;
using MV.DomainLayer.Exceptions;

namespace MV.ApplicationLayer.Services;

/// <summary>
/// OTP xác thực giao dịch lớn cho học sinh tự đăng ký — xem <see cref="ILargeTransactionOtpService"/>
/// cho cơ chế lưu/gửi mã. Phần này chỉ lo việc soi đúng booking, kiểm nó là booking tự đăng ký
/// (Parentid null), và lấy SĐT phụ huynh đã lưu để gửi tới.
/// </summary>
public partial class BookingService
{
    public async Task SendPaymentOtpAsync(int bookingId, string userId, string phase)
    {
        var booking = await GetSelfManagedBookingForOtpAsync(bookingId, userId);

        if (string.IsNullOrWhiteSpace(booking.Student?.Parentphone))
            throw new ParentPhoneRequiredForLargeTransactionException();

        await largeTransactionOtpService.SendAsync(bookingId, phase, booking.Student.Parentphone);
    }

    public async Task VerifyPaymentOtpAsync(int bookingId, string userId, string phase, string code)
    {
        // Xác nhận booking vẫn thuộc đúng user/loại booking trước khi cho verify — tránh verify
        // "hộ" cho một booking không phải của mình (dù OTP key đã theo bookingId rồi, đây là
        // lớp bảo vệ thứ hai, rẻ vì đã có sẵn helper).
        await GetSelfManagedBookingForOtpAsync(bookingId, userId);
        await largeTransactionOtpService.VerifyAsync(bookingId, phase, code);
    }

    /// <summary>
    /// Soi booking thuộc đúng user và là booking tự đăng ký (không có Parentid) — OTP giao dịch
    /// lớn chỉ áp dụng cho nhánh này, phụ huynh không bao giờ đi qua luồng này.
    /// </summary>
    private async Task<Booking> GetSelfManagedBookingForOtpAsync(int bookingId, string userId)
    {
        var booking = await bookingRepo.FindForPaymentByUserAsync(bookingId, userId)
            ?? throw new BookingException(BookingErrorCodes.BookingNotFound, "Không tìm thấy booking", 404);

        if (!string.IsNullOrWhiteSpace(booking.Parentid))
            throw new BookingException(
                BookingErrorCodes.InvalidBookingStatus,
                "Booking này không thuộc diện xác thực OTP giao dịch lớn.",
                400);

        return booking;
    }
}
