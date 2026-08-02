using Microsoft.EntityFrameworkCore;
using MV.ApplicationLayer.Interfaces;
using MV.DomainLayer.Entities;

namespace MV.ApplicationLayer.Helpers;

/// <summary>
/// Người thực trả tiền / cần biết về tiền của một booking — phụ huynh nếu đặt hộ (Parentid có
/// giá trị), ngược lại là chính học sinh tự đặt (&gt;= 16 tuổi, Parentid null). Dùng ở mọi nơi cần
/// gửi thông báo/hoàn tiền liên quan thanh toán cho "người trả tiền" của booking — tránh lặp lại
/// lỗi chỉ đọc <c>booking.Parentid</c> trực tiếp, khiến học sinh tự quản lý không nhận được gì.
/// </summary>
public static class BookingPayerResolver
{
    /// <summary>Dùng khi <see cref="Booking.Student"/> đã được nạp (.Include(b =&gt; b.Student)).</summary>
    public static string? Resolve(Booking booking)
    {
        if (!string.IsNullOrWhiteSpace(booking.Parentid))
            return booking.Parentid;
        if (!string.IsNullOrWhiteSpace(booking.Student?.Linkeduserid))
            return booking.Student!.Linkeduserid;
        return booking.Studentid;
    }

    /// <summary>
    /// Dùng khi <see cref="Booking.Student"/> CHƯA được nạp (vd. entity lấy qua FromSqlRaw để
    /// lock hàng, không thể .Include() thêm) — tự truy vấn nhẹ Studentprofiles theo Studentid.
    /// </summary>
    public static async Task<string?> ResolveAsync(IAppDbContext db, Booking booking, CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(booking.Parentid))
            return booking.Parentid;
        if (string.IsNullOrWhiteSpace(booking.Studentid))
            return null;

        var linkedUserId = await db.Studentprofiles.AsNoTracking()
            .Where(s => s.Studentid == booking.Studentid)
            .Select(s => s.Linkeduserid)
            .FirstOrDefaultAsync(ct);

        return !string.IsNullOrWhiteSpace(linkedUserId) ? linkedUserId : booking.Studentid;
    }
}
