using Microsoft.EntityFrameworkCore;
using MV.ApplicationLayer.Interfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.Helpers;

namespace MV.ApplicationLayer.Helpers
{
    /// <summary>
    /// Guard dùng chung: chặn chỉnh sửa lịch rảnh / package khi tutor đã có buổi dạy
    /// (ClassSession) tương lai còn hiệu lực — tránh phá vỡ buổi đã cam kết với học sinh.
    /// Là NGUỒN SỰ THẬT DUY NHẤT cho quy tắc "slot đã có buổi dạy thì không cho đổi giờ".
    /// </summary>
    public static class TutorScheduleGuard
    {
        /// <summary>Một buổi dạy đã cam kết (giờ UTC).</summary>
        public readonly record struct CommittedSession(DateTime Start, DateTime End);

        /// <summary>
        /// Lấy các buổi dạy TƯƠNG LAI còn hiệu lực của tutor (đã đặt, chưa hủy/hoàn tất/vắng).
        /// Truyền packageId để chỉ lấy buổi thuộc 1 package cụ thể.
        /// </summary>
        public static async Task<List<CommittedSession>> GetFutureCommittedSessionsAsync(
            IAppDbContext context, string tutorId, int? packageId = null)
        {
            var now = TimeZoneHelper.UtcNow;

            var query = context.ClassSessions
                .Where(l => l.Tutorid == tutorId
                    && l.Scheduledstart > now
                    && l.Status != ClassSessionStatus.Cancelled
                    && l.Status != ClassSessionStatus.CancelledNoshow
                    && l.Status != ClassSessionStatus.Completed
                    && l.Status != ClassSessionStatus.NoShow);

            if (packageId.HasValue)
                query = query.Where(l => l.Booking != null && l.Booking.Packageid == packageId.Value);

            var rows = await query
                .Select(l => new { l.Scheduledstart, l.Scheduledend })
                .ToListAsync();

            return rows.Select(r => new CommittedSession(r.Scheduledstart, r.Scheduledend)).ToList();
        }

        /// <summary>
        /// Tập packageId của tutor đang CÒN buổi dạy tương lai chưa hoàn tất.
        /// Dùng để gắn cờ "package đã có booking" cho FE (1 truy vấn, tránh N+1).
        /// </summary>
        public static async Task<HashSet<int>> GetPackageIdsWithFutureSessionsAsync(
            IAppDbContext context, string tutorId)
        {
            var now = TimeZoneHelper.UtcNow;
            var ids = await context.ClassSessions
                .Where(l => l.Tutorid == tutorId
                    && l.Scheduledstart > now
                    && l.Status != ClassSessionStatus.Cancelled
                    && l.Status != ClassSessionStatus.CancelledNoshow
                    && l.Status != ClassSessionStatus.Completed
                    && l.Status != ClassSessionStatus.NoShow
                    && l.Booking != null && l.Booking.Packageid != null)
                .Select(l => l.Booking!.Packageid!.Value)
                .Distinct()
                .ToListAsync();
            return ids.ToHashSet();
        }

        /// <summary>
        /// Có buổi dạy nào rơi vào khung tuần (thứ ISO 1-7, giờ UTC) này không?
        /// Dùng để chặn đổi/xóa 1 slot lịch rảnh cụ thể.
        /// </summary>
        public static bool OverlapsWeeklySlot(
            IEnumerable<CommittedSession> sessions, int isoDayOfWeek, TimeSpan slotStart, TimeSpan slotEnd)
        {
            return sessions.Any(l => MatchesWeeklySlot(l, isoDayOfWeek, slotStart, slotEnd));
        }

        /// <summary>
        /// Trong tập buổi dạy, có buổi nào KHÔNG được bất kỳ slot mới nào bao phủ không?
        /// Dùng cho luồng thay-toàn-bộ-lịch: nếu còn buổi "mồ côi" thì phải chặn.
        /// Mỗi slot mới: (isoDay, start, end) theo giờ UTC.
        /// </summary>
        public static bool HasOrphanedSession(
            IEnumerable<CommittedSession> sessions,
            IEnumerable<(int IsoDay, TimeSpan Start, TimeSpan End)> newSlots)
        {
            var slots = newSlots.ToList();
            return sessions.Any(l =>
                !slots.Any(s => MatchesWeeklySlot(l, s.IsoDay, s.Start, s.End)));
        }

        private static bool MatchesWeeklySlot(CommittedSession l, int isoDayOfWeek, TimeSpan slotStart, TimeSpan slotEnd)
        {
            var iso = l.Start.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)l.Start.DayOfWeek;
            return iso == isoDayOfWeek
                && l.Start.TimeOfDay < slotEnd
                && l.End.TimeOfDay > slotStart;
        }
    }
}
