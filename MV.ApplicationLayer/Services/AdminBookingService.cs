using Microsoft.EntityFrameworkCore;
using MV.ApplicationLayer.Interfaces;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.ResponseModel.Admin;
using MV.DomainLayer.Helpers;

namespace MV.ApplicationLayer.Services;

public class AdminBookingService(IAppDbContext context) : IAdminBookingService
{
    public async Task<AdminBookingListResponse> GetAdminBookingsAsync(
        int page,
        int pageSize,
        string? status,
        string? teachingMode,
        string? tutorId,
        string? parentId,
        int? subjectId,
        DateTime? from,
        DateTime? to,
        string? search,
        CancellationToken ct = default)
    {
        // ── Build base query: Bookings JOIN các bảng liên quan ──────────────
        // Dùng LINQ join thủ công để tránh N+1 và kiểm soát chính xác các field cần lấy.
        // Pattern: anonymous-type projection (SQL) → in-memory map (C# với VietnamTimeHelper)

        var bookingsQuery = context.Bookings.AsNoTracking();

        // ── Áp filter trực tiếp trên Bookings table (index-friendly) ─────────
        if (!string.IsNullOrWhiteSpace(status))
            bookingsQuery = bookingsQuery.Where(b => b.Status == status);

        if (!string.IsNullOrWhiteSpace(tutorId))
            bookingsQuery = bookingsQuery.Where(b => b.Tutorid == tutorId);

        if (!string.IsNullOrWhiteSpace(parentId))
            bookingsQuery = bookingsQuery.Where(b => b.Parentid == parentId);

        if (subjectId.HasValue)
            bookingsQuery = bookingsQuery.Where(b => b.Tutorsubjectgradeprice != null &&
                                                     b.Tutorsubjectgradeprice.Subjectid == subjectId.Value);

        if (from.HasValue)
            bookingsQuery = bookingsQuery.Where(b => b.Createdat >= from.Value);

        if (to.HasValue)
            bookingsQuery = bookingsQuery.Where(b => b.Createdat <= to.Value);

        // ── JOIN với Users (tutor), Tutorprofiles, Studentprofiles, Subjects ──
        var joined = bookingsQuery
            .Join(context.Users.AsNoTracking(),
                b  => b.Tutorid,
                tu => tu.Userid,
                (b, tu) => new { b, TutorUser = tu })
            .Join(context.Tutorprofiles.AsNoTracking(),
                x  => x.b.Tutorid,
                tp => tp.Tutorid,
                (x, tp) => new { x.b, x.TutorUser, TutorProfile = tp })
            // Parent là nullable — dùng GroupJoin (LEFT JOIN) để không mất booking
            .GroupJoin(context.Users.AsNoTracking(),
                x  => x.b.Parentid,
                pu => pu.Userid,
                (x, parents) => new { x.b, x.TutorUser, x.TutorProfile, Parents = parents })
            .SelectMany(
                x => x.Parents.DefaultIfEmpty(),
                (x, pu) => new { x.b, x.TutorUser, x.TutorProfile, ParentUser = pu })
            // Student: LEFT JOIN Studentprofiles
            .GroupJoin(context.Studentprofiles.AsNoTracking(),
                x  => x.b.Studentid,
                sp => sp.Studentid,
                (x, students) => new { x.b, x.TutorUser, x.TutorProfile, x.ParentUser, Students = students })
            .SelectMany(
                x => x.Students.DefaultIfEmpty(),
                (x, sp) => new { x.b, x.TutorUser, x.TutorProfile, x.ParentUser, StudentProfile = sp })
            .Select(x => new
            {
                x.b,
                x.TutorUser,
                x.TutorProfile,
                x.ParentUser,
                x.StudentProfile,
                PaymentCode = context.PaymentRequests
                    .Where(r => r.Bookingid == x.b.Bookingid)
                    .OrderByDescending(r => r.Createdat)
                    .ThenByDescending(r => r.Paymentrequestid)
                    .Select(r => r.Paymentlinkid)
                    .FirstOrDefault()
            });

        // ── Filter theo search (phải sau join để match tên) ──────────────────
        if (!string.IsNullOrWhiteSpace(search))
        {
            var kw = search.ToLower();
            joined = joined.Where(x =>
                (x.TutorUser.Fullname != null && x.TutorUser.Fullname.ToLower().Contains(kw)) ||
                (x.ParentUser != null && x.ParentUser.Fullname != null && x.ParentUser.Fullname.ToLower().Contains(kw)) ||
                (x.ParentUser != null && x.ParentUser.Email != null && x.ParentUser.Email.ToLower().Contains(kw)) ||
                (x.PaymentCode != null && x.PaymentCode.ToLower().Contains(kw)));
        }

        // ── Count trước khi phân trang ────────────────────────────────────────
        var totalCount = await joined.CountAsync(ct);

        // ── Fetch trang (raw projection) ──────────────────────────────────────
        var raw = await joined
            .OrderByDescending(x => x.b.Createdat)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new
            {
                // Booking core
                x.b.Bookingid,
                x.b.Status,
                x.b.Paymentstatus,
                Teachingmode = TeachingMode.Online,
                x.PaymentCode,
                // Tutor
                TutorId      = x.TutorUser.Userid,
                TutorName    = x.TutorUser.Fullname,
                TutorAvatar  = x.TutorUser.Avatarurl,
                // Parent
                ParentId     = x.ParentUser != null ? x.ParentUser.Userid    : null,
                ParentName   = x.ParentUser != null ? x.ParentUser.Fullname  : null,
                ParentEmail  = x.ParentUser != null ? x.ParentUser.Email     : null,
                // Student
                StudentId    = x.StudentProfile != null ? x.StudentProfile.Studentid  : null,
                StudentName  = x.StudentProfile != null ? x.StudentProfile.Fullname   : null,
                GradeLevel   = x.StudentProfile != null && x.StudentProfile.GradelevelNavigation != null
                    ? x.StudentProfile.GradelevelNavigation.Gradename
                    : null,
                // Subject
                SubjectId    = x.b.Tutorsubjectgradeprice != null ? (int?)x.b.Tutorsubjectgradeprice.Subjectid : null,
                SubjectName  = x.b.Tutorsubjectgradeprice != null && x.b.Tutorsubjectgradeprice.Subject != null
                    ? x.b.Tutorsubjectgradeprice.Subject.Subjectname
                    : null,
                // Financial
                Price = x.b.Totalamount,
                x.b.Discountapplied,
                x.b.Finalprice,
                x.b.Platformfee,
                // Progress
                Sessioncount = x.b.Totalsessions,
                x.b.Sessionsremaining,
                // Dates
                x.b.Startdate,
                x.b.Createdat,
                x.b.Cancelledat,
                x.b.Cancellationreason
            })
            .ToListAsync(ct);

        // ── Batch load classSession counts cho các booking trong trang ──────────────
        var bookingIds = raw.Select(x => x.Bookingid).ToList();

        var classSessionCounts = await context.ClassSessions
            .AsNoTracking()
            .Where(l => l.Bookingid != null && bookingIds.Contains(l.Bookingid.Value))
            .GroupBy(l => l.Bookingid!.Value)
            .Select(g => new
            {
                BookingId  = g.Key,
                Total      = g.Count(),
                Completed  = g.Count(l => l.Status == ClassSessionStatus.Completed)
            })
            .ToListAsync(ct);

        var classSessionCountDict = classSessionCounts.ToDictionary(x => x.BookingId);

        // ── In-memory map với VietnamTimeHelper ───────────────────────────────
        var items = raw.Select(x =>
        {
            classSessionCountDict.TryGetValue(x.Bookingid, out var lc);
            return new AdminBookingListItem
            {
                BookingId        = x.Bookingid,
                Status           = x.Status,
                PaymentStatus    = x.Paymentstatus,
                TeachingMode     = TeachingMode.Online,
                PaymentCode      = x.PaymentCode,
                // Tutor
                TutorId          = x.TutorId,
                TutorName        = x.TutorName,
                TutorAvatarUrl   = x.TutorAvatar,
                // Parent
                ParentId         = x.ParentId,
                ParentName       = x.ParentName,
                ParentEmail      = x.ParentEmail,
                // Student
                StudentId        = x.StudentId,
                StudentName      = x.StudentName,
                GradeLevel       = x.GradeLevel,
                // Subject
                SubjectId        = x.SubjectId,
                SubjectName      = x.SubjectName,
                // Financial
                Price            = x.Price,
                DiscountApplied  = x.Discountapplied,
                FinalPrice       = x.Finalprice,
                PlatformFee      = x.Platformfee,
                // Progress
                SessionCount     = x.Sessioncount,
                SessionsRemaining = x.Sessionsremaining,
                ClassSessionsTotal     = lc?.Total    ?? 0,
                ClassSessionsCompleted = lc?.Completed ?? 0,
                // Dates — tất cả convert sang VN time
                StartDate        = x.Startdate,
                CreatedAt        = x.Createdat,
                CancelledAt      = x.Cancelledat,
                CancellationReason = x.Cancellationreason
            };
        }).ToList();

        return new AdminBookingListResponse
        {
            Items      = items,
            TotalCount = totalCount,
            Page       = page,
            PageSize   = pageSize
        };
    }

    public async Task<AdminBookingDetailResponse?> GetAdminBookingDetailAsync(
        int bookingId,
        CancellationToken ct = default)
    {
        // ── JOIN: Bookings → Users(tutor), LEFT JOIN parent/student/subject ──
        var row = await context.Bookings.AsNoTracking()
            .Where(b => b.Bookingid == bookingId)
            .Join(context.Users.AsNoTracking(),
                b  => b.Tutorid,
                tu => tu.Userid,
                (b, tu) => new { b, TutorUser = tu })
            .GroupJoin(context.Users.AsNoTracking(),
                x  => x.b.Parentid,
                pu => pu.Userid,
                (x, parents) => new { x.b, x.TutorUser, Parents = parents })
            .SelectMany(
                x => x.Parents.DefaultIfEmpty(),
                (x, pu) => new { x.b, x.TutorUser, ParentUser = pu })
            .GroupJoin(context.Studentprofiles.AsNoTracking(),
                x  => x.b.Studentid,
                sp => sp.Studentid,
                (x, students) => new { x.b, x.TutorUser, x.ParentUser, Students = students })
            .SelectMany(
                x => x.Students.DefaultIfEmpty(),
                (x, sp) => new { x.b, x.TutorUser, x.ParentUser, StudentProfile = sp })
            .Select(x => new { x.b, x.TutorUser, x.ParentUser, x.StudentProfile })
            .Select(x => new
            {
                // Booking core
                x.b.Bookingid,
                x.b.Status,
                Teachingmode = TeachingMode.Online,
                PaymentCode = context.PaymentRequests
                    .Where(r => r.Bookingid == x.b.Bookingid)
                    .OrderByDescending(r => r.Createdat)
                    .ThenByDescending(r => r.Paymentrequestid)
                    .Select(r => r.Paymentlinkid)
                    .FirstOrDefault(),
                // Location fields
                x.b.Locationdetail,
                x.b.Locationward,
                x.b.Locationdistrict,
                x.b.Locationcity,
                // Tutor
                TutorId     = x.TutorUser.Userid,
                TutorName   = x.TutorUser.Fullname,
                TutorAvatar = x.TutorUser.Avatarurl,
                // Parent
                ParentId    = x.ParentUser != null ? x.ParentUser.Userid   : null,
                ParentName  = x.ParentUser != null ? x.ParentUser.Fullname : null,
                ParentEmail = x.ParentUser != null ? x.ParentUser.Email    : null,
                // Student
                StudentId   = x.StudentProfile != null ? x.StudentProfile.Studentid  : null,
                StudentName = x.StudentProfile != null ? x.StudentProfile.Fullname   : null,
                GradeLevel  = x.StudentProfile != null && x.StudentProfile.GradelevelNavigation != null
                    ? x.StudentProfile.GradelevelNavigation.Gradename
                    : null,
                // Subject
                SubjectId   = x.b.Tutorsubjectgradeprice != null ? (int?)x.b.Tutorsubjectgradeprice.Subjectid : null,
                SubjectName = x.b.Tutorsubjectgradeprice != null && x.b.Tutorsubjectgradeprice.Subject != null
                    ? x.b.Tutorsubjectgradeprice.Subject.Subjectname
                    : null,
                // Financial
                Price = x.b.Totalamount,
                x.b.Discountapplied,
                x.b.Finalprice,
                x.b.Paymentstatus,
                x.b.Depositamount,
                x.b.Depositpaidat,
                x.b.Remainingamount,
                x.b.Remainingpaidat,
                x.b.Platformfee,
                x.b.Tutorfee,
                x.b.Parentfee,
                x.b.Escrowstatus,
                x.b.Refundamount,
                x.b.Refundstatus,
                x.b.Paymentdueat,
                // Progress
                Sessioncount = x.b.Totalsessions,
                x.b.Startdate,
                // Dates
                x.b.Createdat,
                x.b.Updatedat,
                // Cancellation
                x.b.Cancelledat,
                x.b.Cancellationreason
            })
            .FirstOrDefaultAsync(ct);

        if (row == null) return null;

        // ── Load all classSessions for this booking ─────────────────────────────────
        var classSessions = await context.ClassSessions
            .AsNoTracking()
            .Where(l => l.Bookingid == bookingId)
            .OrderBy(l => l.Scheduledstart)
            .Select(l => new
            {
                l.Classsessionid,
                l.Status,
                l.Scheduledstart,
                l.Scheduledend,
                l.Realstart,
                l.Realend,
                l.Lessonprice,
                l.Issettled,
                l.Ismakeup,
                l.Tutornotes,
                l.Meetinglink
            })
            .ToListAsync(ct);

        var completedClassSessions = classSessions.Count(l => l.Status == ClassSessionStatus.Completed);

        // ── Build composite location string ───────────────────────────────────
        var locationParts = new[] { row.Locationdetail, row.Locationward, row.Locationdistrict, row.Locationcity }
            .Where(p => !string.IsNullOrWhiteSpace(p));
        var locationString = string.Join(", ", locationParts);

        // ── Build timeline from booking timestamps ────────────────────────────
        var timelineRaw = new List<(string Label, string? Status, DateTime? At)>
        {
            ("Booking created",         BookingStatus.PendingTutor,            row.Createdat),
            ("Deposit paid",            BookingStatus.DepositPaid,             row.Depositpaidat),
            ("Full payment received",   BookingStatus.Paid,                    row.Remainingpaidat),
            ("Booking cancelled",       BookingStatus.Cancelled,               row.Cancelledat),
        };
        var timeline = timelineRaw
            .Where(e => e.At.HasValue)
            .OrderBy(e => e.At)
            .Select(e => new BookingTimelineEvent
            {
                Label      = e.Label,
                Status     = e.Status,
                OccurredAt = e.At!.Value
            })
            .ToList();

        // ── Build classSession list ─────────────────────────────────────────────────
        var classSessionItems = classSessions
            .Select((l, idx) => new AdminBookingClassSessionItem
            {
                ClassSessionId       = l.Classsessionid,
                ClassSessionNumber   = idx + 1,
                Status         = l.Status,
                ScheduledStart = l.Scheduledstart,
                ScheduledEnd   = l.Scheduledend,
                RealStart      = l.Realstart,
                RealEnd        = l.Realend,
                ClassSessionPrice    = l.Lessonprice,
                IsSettled      = l.Issettled,
                IsMakeup       = l.Ismakeup,
                TutorNotes     = l.Tutornotes,
                MeetingLink    = l.Meetinglink
            })
            .ToList();

        // ── In-memory map ─────────────────────────────────────────────────────
        return new AdminBookingDetailResponse
        {
            BookingId   = row.Bookingid,
            Status      = row.Status,
            TeachingMode = TeachingMode.Online,
            PaymentCode = row.PaymentCode,
            Location    = string.IsNullOrEmpty(locationString) ? null : locationString,

            Tutor = new AdminBookingPartyInfo
            {
                Id        = row.TutorId,
                Name      = row.TutorName,
                AvatarUrl = row.TutorAvatar
            },
            Parent = row.ParentId != null ? new AdminBookingPartyInfo
            {
                Id    = row.ParentId,
                Name  = row.ParentName,
                Email = row.ParentEmail
            } : null,
            Student = row.StudentId != null ? new AdminBookingStudentInfo
            {
                Id         = row.StudentId,
                Name       = row.StudentName,
                GradeLevel = row.GradeLevel
            } : null,
            Subject = row.SubjectId != null ? new AdminBookingSubjectInfo
            {
                Id   = row.SubjectId,
                Name = row.SubjectName
            } : null,

            Financials = new AdminBookingFinancials
            {
                Price           = row.Price,
                DiscountApplied = row.Discountapplied,
                FinalPrice      = row.Finalprice,
                PaymentStatus   = row.Paymentstatus
            },
            Progress = new AdminBookingProgress
            {
                TotalSessions     = row.Sessioncount,
                CompletedSessions = completedClassSessions,
                StartDate         = row.Startdate
            },
            Cancellation = new AdminBookingCancellation
            {
                IsCancelled = row.Status is BookingStatus.Cancelled or BookingStatus.CancelledNoshow,
                CancelledAt = row.Cancelledat,
                Reason      = row.Cancellationreason
            },

            CreatedAt = row.Createdat,
            UpdatedAt = row.Updatedat,

            Timeline = timeline,
            ClassSessions  = classSessionItems,
            PaymentBreakdown = new AdminBookingPaymentBreakdown
            {
                OriginalPrice    = row.Price,
                DiscountApplied  = row.Discountapplied,
                FinalPrice       = row.Finalprice,
                DepositAmount    = row.Depositamount,
                DepositPaidAt    = row.Depositpaidat,
                RemainingAmount  = row.Remainingamount,
                RemainingPaidAt  = row.Remainingpaidat,
                PlatformFee      = row.Platformfee,
                TutorFee         = row.Tutorfee,
                ParentFee        = row.Parentfee,
                EscrowStatus     = row.Escrowstatus,
                RefundAmount     = row.Refundamount,
                RefundStatus     = row.Refundstatus,
                PaymentStatus    = row.Paymentstatus,
                PaymentDueAt     = row.Paymentdueat
            }
        };
    }
}
