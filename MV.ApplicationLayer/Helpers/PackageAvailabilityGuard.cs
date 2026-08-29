using Microsoft.EntityFrameworkCore;
using MV.ApplicationLayer.Interfaces;
using MV.DomainLayer.Entities;

namespace MV.ApplicationLayer.Helpers;

/// <summary>
/// Giữ cho khung cố định của gói học và lịch rảnh không mâu thuẫn nhau — theo CẢ HAI CHIỀU.
///
/// Vì sao ràng buộc này chính đáng, trong khi ràng buộc "lịch rảnh vs buổi đã đặt" thì không
/// (xem comment trong TutorAvailabilityService): khung cố định là LỜI HỨA sẽ nhận booking vào giờ
/// đó, còn lịch rảnh là nguồn sự thật quyết định booking có được chấp nhận hay không
/// (BookingService.ValidateSlotsAsync). Hai thứ lệch nhau thì gói vẫn hiện ra cho phụ huynh xem
/// nhưng đặt là lỗi — và người phát hiện lại là phụ huynh, không phải gia sư.
///
/// Buổi ĐÃ đặt thì khác hẳn: nó đã là cam kết chốt, nằm trong class_sessions với giờ cụ thể, và
/// không đường nào đọc lịch rảnh để chặn nó.
///
/// Chỉ gói ĐANG ACTIVE và loại FIXED mới ràng buộc. Gói tắt không đặt được nên không có gì để
/// bảo vệ; gói FLEXIBLE thì không hứa giờ nào cả (phụ huynh tự chọn slot, ValidateSlotsAsync kiểm
/// tra lúc đặt) — và trong dữ liệu cũ có những gói flexible mang fixed slot rác, trái với chính
/// luật của ValidateTutorPackageRequest, nên lọc theo loại gói còn tránh chặn nhầm vì rác đó.
/// </summary>
public static class PackageAvailabilityGuard
{
    /// <summary>Một khung cố định của gói, kèm tên gói để dựng thông báo lỗi đọc được.</summary>
    public readonly record struct PackageSlot(
        int PackageId,
        string PackageName,
        int DayOfWeek,
        TimeSpan Start,
        TimeSpan End);

    /// <summary>Một khung rảnh (giờ UTC).</summary>
    public readonly record struct AvailabilityWindow(int DayOfWeek, TimeSpan Start, TimeSpan End);

    /// <summary>
    /// Khung cố định của mọi gói đang active của gia sư. <paramref name="excludePackageId"/> để bỏ
    /// qua chính gói đang được sửa (khung cũ của nó sắp bị thay).
    /// </summary>
    public static async Task<List<PackageSlot>> GetActivePackageSlotsAsync(
        IAppDbContext context,
        string tutorId,
        int? excludePackageId = null,
        CancellationToken ct = default)
    {
        var query = context.Tutorpackagefixedslots
            .AsNoTracking()
            .Where(s => s.Package.Tutorid == tutorId
                && s.Package.Isactive
                && s.Package.Packagetype == Tutorpackage.FixedPackageType);

        if (excludePackageId.HasValue)
            query = query.Where(s => s.Packageid != excludePackageId.Value);

        var rows = await query
            .Select(s => new { s.Packageid, s.Package.Name, s.Dayofweek, s.Starttime, s.Endtime })
            .ToListAsync(ct);

        return rows
            .Select(r => new PackageSlot(
                r.Packageid, r.Name, r.Dayofweek, r.Starttime.ToTimeSpan(), r.Endtime.ToTimeSpan()))
            .ToList();
    }

    /// <summary>
    /// Khung cố định đầu tiên KHÔNG được bất kỳ khung rảnh nào bao trọn, hoặc null nếu tất cả đều
    /// hợp lệ. "Bao trọn" chứ không phải "giao nhau": gói hứa dạy từ 19:00 đến 21:00 thì lịch rảnh
    /// phải phủ hết khoảng đó, phủ một nửa vẫn là không đặt được.
    ///
    /// Một khung cố định có thể được ghép từ NHIỀU khung rảnh liền nhau (vd 18:00-20:00 và
    /// 20:00-22:00), nên phải gộp các khung rảnh liền/chồng nhau trước khi kiểm tra.
    /// </summary>
    public static PackageSlot? FindSlotOutsideAvailability(
        IEnumerable<PackageSlot> packageSlots,
        IEnumerable<AvailabilityWindow> availabilities)
    {
        var mergedByDay = MergeByDay(availabilities);

        foreach (var slot in packageSlots)
        {
            if (!mergedByDay.TryGetValue(slot.DayOfWeek, out var windows))
                return slot;

            if (!windows.Any(w => w.Start <= slot.Start && w.End >= slot.End))
                return slot;
        }

        return null;
    }

    /// <summary>Gộp các khung rảnh liền kề hoặc chồng nhau trong cùng một ngày thành khoảng liên tục.</summary>
    private static Dictionary<int, List<(TimeSpan Start, TimeSpan End)>> MergeByDay(
        IEnumerable<AvailabilityWindow> availabilities)
    {
        var result = new Dictionary<int, List<(TimeSpan Start, TimeSpan End)>>();

        foreach (var group in availabilities.GroupBy(a => a.DayOfWeek))
        {
            var merged = new List<(TimeSpan Start, TimeSpan End)>();

            foreach (var window in group.OrderBy(a => a.Start))
            {
                if (merged.Count > 0 && window.Start <= merged[^1].End)
                {
                    // Chồng hoặc chạm nhau → kéo dài khoảng đang mở thay vì mở khoảng mới.
                    if (window.End > merged[^1].End)
                        merged[^1] = (merged[^1].Start, window.End);
                }
                else
                {
                    merged.Add((window.Start, window.End));
                }
            }

            result[group.Key] = merged;
        }

        return result;
    }
}
