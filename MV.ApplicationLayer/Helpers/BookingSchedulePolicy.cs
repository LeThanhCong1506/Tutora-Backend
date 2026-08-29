namespace MV.ApplicationLayer.Helpers;

/// <summary>
/// Ràng buộc về CÁCH PHÂN BỔ các buổi học khi phụ huynh tự chọn lịch (gói linh hoạt).
///
/// Trước đây mỗi slot chỉ được kiểm tra độc lập (nằm trong lịch rảnh, không trùng buổi đã khoá,
/// không ở quá khứ), nên quan hệ GIỮA các slot hoàn toàn không bị ràng buộc: gói "3 buổi/tuần"
/// có thể bị đặt cả 3 buổi vào cùng một ngày, hoặc rải 1 buổi/tuần trong 3 tuần. Con số
/// <c>Sessionsperweek</c> gia sư khai chỉ được dùng lúc cấu hình giá, không ai thực thi lúc đặt.
///
/// Chỉ áp cho gói LINH HOẠT. Gói cố định tự sinh lịch từ khung tuần của chính nó
/// (GenerateFixedPackageSlots) nên phụ huynh không chọn gì, và ép thêm ràng buộc ở đó sẽ làm hỏng
/// các gói đang chạy có số khung/tuần khác với con số ở bảng giá.
/// </summary>
public static class BookingSchedulePolicy
{
    /// <summary>Lệch múi giờ VN so với UTC. Tuần và ngày phải tính theo giờ người dùng nhìn thấy.</summary>
    private const int VietnamUtcOffsetHours = 7;

    /// <summary>Tối đa một buổi mỗi ngày — hai buổi liền trong ngày không phải ý đồ của "N buổi/tuần".</summary>
    public const int MaxSessionsPerDay = 1;

    /// <summary>
    /// Kiểm tra phân bổ buổi học. Ném <see cref="InvalidOperationException"/> kèm thông điệp cho
    /// người dùng nếu vi phạm; caller bọc lại thành BookingException với mã lỗi phù hợp.
    ///
    /// Luật: mỗi tuần đúng <paramref name="sessionsPerWeek"/> buổi, RIÊNG tuần cuối được phép ít
    /// hơn — tổng số buổi hiếm khi chia hết cho số buổi mỗi tuần (5 buổi với 2 buổi/tuần là
    /// 2+2+1), nếu bắt tuần nào cũng đủ thì phần lớn tổ hợp trở thành bất khả thi.
    /// </summary>
    public static void EnsureValidDistribution(
        IReadOnlyList<DateTime> slotStartsUtc,
        int sessionsPerWeek)
    {
        if (slotStartsUtc.Count == 0 || sessionsPerWeek < 1) return;

        var local = slotStartsUtc
            .Select(s => s.AddHours(VietnamUtcOffsetHours))
            .OrderBy(s => s)
            .ToList();

        // ── Tối đa 1 buổi/ngày ──
        var dayClash = local
            .GroupBy(DateOnly.FromDateTime)
            .FirstOrDefault(g => g.Count() > MaxSessionsPerDay);

        if (dayClash != null)
            throw new InvalidOperationException(
                $"Ngày {dayClash.Key:dd/MM/yyyy} có {dayClash.Count()} buổi học. "
                + "Mỗi ngày chỉ được xếp tối đa 1 buổi, vui lòng chọn ngày khác cho các buổi còn lại.");

        // ── Đúng N buổi mỗi tuần (tuần cuối được phép thiếu) ──
        var weeks = local
            .GroupBy(StartOfWeek)
            .OrderBy(g => g.Key)
            .ToList();

        var firstWeek = weeks[0].Key;
        var lastWeek = weeks[^1].Key;

        foreach (var week in weeks)
        {
            var count = week.Count();
            if (count == sessionsPerWeek) continue;

            // Tuần ĐẦU và tuần CUỐI được phép thiếu; thừa thì không, ở bất kỳ tuần nào.
            //
            // Tuần cuối: tổng số buổi hiếm khi chia hết cho số buổi mỗi tuần (5 buổi với 2
            // buổi/tuần là 2+2+1).
            //
            // Tuần đầu: các ngày trong mẫu đã trôi qua, hoặc chưa đủ xa để đặt, đều bị loại khỏi
            // tuần này. Không nới thì đặt lịch chiều thứ Hai cho mẫu 2-4-6 sẽ bị đẩy sang tuần
            // sau nguyên vẹn, mất 5 ngày chỉ vì một ô đầu tuần không dùng được.
            if ((week.Key == firstWeek || week.Key == lastWeek) && count < sessionsPerWeek) continue;

            throw new InvalidOperationException(
                $"Tuần {week.Key:dd/MM/yyyy} có {count} buổi, trong khi gia sư nhận dạy "
                + $"{sessionsPerWeek} buổi mỗi tuần. Vui lòng xếp lại lịch cho đúng.");
        }
    }

    /// <summary>Thứ Hai của tuần chứa <paramref name="localTime"/>, theo giờ VN.</summary>
    private static DateOnly StartOfWeek(DateTime localTime)
    {
        var date = DateOnly.FromDateTime(localTime);
        // DayOfWeek: Sunday = 0. Quy về ISO (Thứ Hai = 0) rồi lùi về đầu tuần.
        var offset = ((int)date.DayOfWeek + 6) % 7;
        return date.AddDays(-offset);
    }
}
