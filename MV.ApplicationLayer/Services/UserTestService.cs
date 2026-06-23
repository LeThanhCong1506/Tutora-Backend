using Microsoft.EntityFrameworkCore;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.ApplicationLayer.Interfaces;

namespace MV.ApplicationLayer.Services;

public class UserTestService : IUserTestService
{
    private readonly IAppDbContext _context;

    public UserTestService(IAppDbContext context)
    {
        _context = context;
    }

    public async Task<(bool Success, string Message)> DeleteUserForTestAsync(string userId)
    {
        // ========== 1. VALIDATE USER TON TAI ==========
        var user = await _context.Users
            .FirstOrDefaultAsync(x => x.Userid == userId);

        if (user == null)
            return (false, "User khong ton tai");

        // Kiem tra role tu Primaryrole
        var userRole = user.Primaryrole ?? "";

        // ========== 2. XOA NEU LA TUTOR ==========
        if (userRole.Equals(UserRole.Tutor, StringComparison.OrdinalIgnoreCase) ||
            await _context.Tutorprofiles.AnyAsync(t => t.Tutorid == userId))
        {
            // Lấy các LessonId của tutor để xóa Lessonreport và Dispute
            var tutorLessonIds = await _context.Lessons
                .Where(l => l.Tutorid == userId)
                .Select(l => l.Lessonid)
                .ToListAsync();

            // Lấy các BookingId của tutor để xóa các bảng con
            var tutorBookingIds = await _context.Bookings
                .Where(b => b.Tutorid == userId)
                .Select(b => b.Bookingid)
                .ToListAsync();

            if (tutorLessonIds.Any())
            {
                // Xóa Lessonreport của các lesson
                await _context.Lessonreports
                    .Where(lr => tutorLessonIds.Contains(lr.Lessonid ?? 0))
                    .ExecuteDeleteAsync();

                // Xóa Dispute liên quan đến lesson
                await _context.Disputes
                    .Where(d => tutorLessonIds.Contains(d.Lessonid ?? 0))
                    .ExecuteDeleteAsync();
            }

            if (tutorBookingIds.Any())
            {
                // Xóa Chatchannel và Chatmessage của booking
                var channelIds = await _context.Chatchannels
                    .Where(c => tutorBookingIds.Contains(c.Bookingid ?? 0))
                    .Select(c => c.Channelid)
                    .ToListAsync();

                if (channelIds.Any())
                {
                    await _context.Chatmessages
                        .Where(m => channelIds.Contains(m.Channelid ?? 0))
                        .ExecuteDeleteAsync();

                    await _context.Chatchannels
                        .Where(c => channelIds.Contains(c.Channelid))
                        .ExecuteDeleteAsync();
                }

                // Xóa Feedback của booking
                await _context.Feedbacks
                    .Where(f => tutorBookingIds.Contains(f.Bookingid ?? 0))
                    .ExecuteDeleteAsync();

                // Xóa Dispute của booking
                await _context.Disputes
                    .Where(d => tutorBookingIds.Contains(d.Bookingid ?? 0))
                    .ExecuteDeleteAsync();
            }

            // Xóa Lessons của tutor
            await _context.Lessons
                .Where(l => l.Tutorid == userId)
                .ExecuteDeleteAsync();

            // Xóa Lessonreport do tutor tạo
            await _context.Lessonreports
                .Where(lr => lr.Createdbytutorid == userId)
                .ExecuteDeleteAsync();

            // Xóa Bookings của tutor
            await _context.Bookings
                .Where(b => b.Tutorid == userId)
                .ExecuteDeleteAsync();

            // Xóa tutor subject-grade prices
            await _context.Tutorsubjectgradeprices
                .Where(ts => ts.Tutorid == userId)
                .ExecuteDeleteAsync();

            // Xóa Tutoravailability
            await _context.Tutoravailabilities
                .Where(ta => ta.Tutorid == userId)
                .ExecuteDeleteAsync();

            // Xóa Tutorprofile
            await _context.Tutorprofiles
                .Where(tp => tp.Tutorid == userId)
                .ExecuteDeleteAsync();
        }

        // ========== 3. XOA NEU LA PARENT ==========
        if (userRole.Equals(UserRole.Parent, StringComparison.OrdinalIgnoreCase) ||
            await _context.Studentprofiles.AnyAsync(s => s.Parentid == userId))
        {
            // Lấy danh sách StudentId của parent
            var studentIds = await _context.Studentprofiles
                .Where(s => s.Parentid == userId)
                .Select(s => s.Studentid)
                .ToListAsync();

            if (studentIds.Any())
            {
                // Lấy LessonId của student
                var studentLessonIds = await _context.Lessons
                    .Where(l => studentIds.Contains(l.Studentid))
                    .Select(l => l.Lessonid)
                    .ToListAsync();

                // Lấy BookingId của student
                var studentBookingIds = await _context.Bookings
                    .Where(b => studentIds.Contains(b.Studentid))
                    .Select(b => b.Bookingid)
                    .ToListAsync();

                if (studentLessonIds.Any())
                {
                    // Xóa Lessonreport
                    await _context.Lessonreports
                        .Where(lr => studentLessonIds.Contains(lr.Lessonid ?? 0))
                        .ExecuteDeleteAsync();

                    // Xóa Dispute liên quan lesson
                    await _context.Disputes
                        .Where(d => studentLessonIds.Contains(d.Lessonid ?? 0))
                        .ExecuteDeleteAsync();
                }

                if (studentBookingIds.Any())
                {
                    // Xóa Chatchannel và Chatmessage
                    var channelIds = await _context.Chatchannels
                        .Where(c => studentBookingIds.Contains(c.Bookingid ?? 0))
                        .Select(c => c.Channelid)
                        .ToListAsync();

                    if (channelIds.Any())
                    {
                        await _context.Chatmessages
                            .Where(m => channelIds.Contains(m.Channelid ?? 0))
                            .ExecuteDeleteAsync();

                        await _context.Chatchannels
                            .Where(c => channelIds.Contains(c.Channelid))
                            .ExecuteDeleteAsync();
                    }

                    // Xóa Feedback
                    await _context.Feedbacks
                        .Where(f => studentBookingIds.Contains(f.Bookingid ?? 0))
                        .ExecuteDeleteAsync();

                    // Xóa Dispute của booking
                    await _context.Disputes
                        .Where(d => studentBookingIds.Contains(d.Bookingid ?? 0))
                        .ExecuteDeleteAsync();
                }

                // Xóa Lessons của student
                await _context.Lessons
                    .Where(l => studentIds.Contains(l.Studentid))
                    .ExecuteDeleteAsync();

                // Xóa Bookings của student
                await _context.Bookings
                    .Where(b => studentIds.Contains(b.Studentid))
                    .ExecuteDeleteAsync();

                // Xóa Studentprofile
                await _context.Studentprofiles
                    .Where(s => s.Parentid == userId)
                    .ExecuteDeleteAsync();
            }

            // Xóa Booking mà parent đặt (Parentid)
            var parentBookingIds = await _context.Bookings
                .Where(b => b.Parentid == userId)
                .Select(b => b.Bookingid)
                .ToListAsync();

            if (parentBookingIds.Any())
            {
                // Lấy LessonId từ booking của parent
                var parentLessonIds = await _context.Lessons
                    .Where(l => parentBookingIds.Contains(l.Bookingid ?? 0))
                    .Select(l => l.Lessonid)
                    .ToListAsync();

                if (parentLessonIds.Any())
                {
                    await _context.Lessonreports
                        .Where(lr => parentLessonIds.Contains(lr.Lessonid ?? 0))
                        .ExecuteDeleteAsync();

                    await _context.Disputes
                        .Where(d => parentLessonIds.Contains(d.Lessonid ?? 0))
                        .ExecuteDeleteAsync();

                    await _context.Lessons
                        .Where(l => parentLessonIds.Contains(l.Lessonid))
                        .ExecuteDeleteAsync();
                }

                // Xóa các bảng liên quan booking
                var channelIds = await _context.Chatchannels
                    .Where(c => parentBookingIds.Contains(c.Bookingid ?? 0))
                    .Select(c => c.Channelid)
                    .ToListAsync();

                if (channelIds.Any())
                {
                    await _context.Chatmessages
                        .Where(m => channelIds.Contains(m.Channelid ?? 0))
                        .ExecuteDeleteAsync();

                    await _context.Chatchannels
                        .Where(c => channelIds.Contains(c.Channelid))
                        .ExecuteDeleteAsync();
                }

                await _context.Feedbacks
                    .Where(f => parentBookingIds.Contains(f.Bookingid ?? 0))
                    .ExecuteDeleteAsync();

                await _context.Disputes
                    .Where(d => parentBookingIds.Contains(d.Bookingid ?? 0))
                    .ExecuteDeleteAsync();

                await _context.Bookings
                    .Where(b => b.Parentid == userId)
                    .ExecuteDeleteAsync();
            }
        }

        // ========== 4. XOA NEU LA STUDENT - LinkedUser ==========
        if (userRole.Equals(UserRole.Student, StringComparison.OrdinalIgnoreCase) ||
            await _context.Studentprofiles.AnyAsync(s => s.Linkeduserid == userId))
        {
            await _context.Studentprofiles
                .Where(s => s.Linkeduserid == userId)
                .ExecuteDeleteAsync();
        }

        // ========== 5. XÓA CÁC BẢNG CHUNG (CHO TẤT CẢ ROLE) ==========

        // 5.1. Xóa Feedback (user gửi hoặc nhận)
        await _context.Feedbacks
            .Where(f => f.Fromuserid == userId || f.Touserid == userId)
            .ExecuteDeleteAsync();

        // 5.2. Xóa Dispute (user tạo hoặc resolve)
        await _context.Disputes
            .Where(d => d.Createdby == userId || d.Resolvedby == userId)
            .ExecuteDeleteAsync();

        // 5.3. Xóa Chatmessage (user gửi)
        await _context.Chatmessages
            .Where(m => m.Senderid == userId)
            .ExecuteDeleteAsync();

        // 5.4. Xóa quan hệ many-to-many Chatchannel-User (chatparticipants)
        // Xóa trực tiếp qua raw SQL để tránh load full entity Chatchannel
        // (cột studentid chưa có trong DB nên không thể ToListAsync)
        await _context.Database.ExecuteSqlRawAsync(
            "DELETE FROM chat_participants WHERE user_id = {0}", userId);

        // 5.5. Xóa Notification
        await _context.Notifications
            .Where(n => n.Userid == userId)
            .ExecuteDeleteAsync();

        // 5.6. Xóa Withdrawalrequest
        await _context.Withdrawalrequests
            .Where(w => w.Userid == userId)
            .ExecuteDeleteAsync();

        // 5.7. Xóa Wallettransaction và Wallet
        var wallet = await _context.Wallets
            .FirstOrDefaultAsync(w => w.Userid == userId);

        if (wallet != null)
        {
            await _context.Wallettransactions
                .Where(wt => wt.Walletid == wallet.Walletid)
                .ExecuteDeleteAsync();

            await _context.Wallets
                .Where(w => w.Userid == userId)
                .ExecuteDeleteAsync();
        }



        // ========== 6. CUỐI CÙNG XÓA USER ==========
        await _context.Users
            .Where(u => u.Userid == userId)
            .ExecuteDeleteAsync();

        return (true, "Xóa user thành công (test)");
    }

    public async Task<(bool Found, string Message)> FixBookingAsync(int bookingId)
    {
        var booking = await _context.Bookings.FindAsync(bookingId);
        if (booking == null)
            return (false, ApiMessages.BookingNotFound);

        booking.Status = BookingStatus.DepositPaid;
        booking.Paymentstatus = MV.DomainLayer.Constants.PaymentStatus.DepositEscrowed;
        booking.Remainingpaidat = null;
        await _context.SaveChangesAsync();
        return (true, "Fixed booking " + bookingId);
    }
}
