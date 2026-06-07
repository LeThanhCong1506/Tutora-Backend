using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MV.ApplicationLayer.Helpers;
using MV.ApplicationLayer.Interfaces;
using MV.ApplicationLayer.RepositoryInterfaces;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.Entities;
using MV.DomainLayer.Exceptions;
using MV.DomainLayer.Helpers;
using System.Data;
using static MV.DomainLayer.Constants.LessonStatus;
using static MV.DomainLayer.Constants.PaymentStatus;

namespace MV.ApplicationLayer.Services;

public partial class BookingService(
    IBookingRepository bookingRepo,
    IStudentRepository studentRepo,
    ITutorRepository tutorRepo,
    IAppDbContext context,          // retained only for: Lessons (conflict check), Subjects, Tutorsubjects, Tutoravailabilities, Promotions, Wallets, Wallettransactions, Notifications
    INotificationService notificationService,
    IChatService chatService,
    ILogger<BookingService> logger) : IBookingService
{
    private const int WeeksPerMonth = 4;
    private const int AvailabilityValidDays = 30;

    public async Task<BookingResponse> CreateBookingAsync(string userId, string userRole, CreateBookingRequest dto)
    {
        if (dto.StartDate.Date < VietnamTimeHelper.Now.Date)
            throw new BookingException(BookingErrorCodes.InvalidStartDate, "Ngày bắt đầu phải là ngày hiện tại hoặc trong tương lai", 400);

        var resolvedStudentId = !string.IsNullOrWhiteSpace(dto.StudentId)
            ? dto.StudentId
            : (userRole == UserRole.Student ? userId : null);

        if (string.IsNullOrWhiteSpace(resolvedStudentId))
            throw new BookingException(BookingErrorCodes.NotStudentOwner, "Vui lòng cung cấp StudentId", 400);

        var student = await studentRepo.FindByStudentOrLinkedUserAsync(resolvedStudentId)
            ?? await studentRepo.GetByIdAndParentAsync(resolvedStudentId, userId)
            ?? throw new BookingException(BookingErrorCodes.NotStudentOwner, "Không tìm thấy học sinh", 404);

        if (userRole == UserRole.Parent && student.Parentid != userId)
            throw new BookingException(BookingErrorCodes.NotStudentOwner, "Học sinh này không thuộc quản lý của phụ huynh", 403);
        if (userRole == UserRole.Student && student.Studentid != userId && student.Linkeduserid != userId)
            throw new BookingException(BookingErrorCodes.NotStudentOwner, "Bạn chỉ có thể đặt lịch cho chính mình", 403);

        var tutor = await context.Tutorprofiles.Include(t => t.Tutor).FirstOrDefaultAsync(t => t.Tutorid == dto.TutorId)
            ?? throw new BookingException(BookingErrorCodes.TutorNotFound, "Không tìm thấy gia sư", 404);
        if (!string.Equals(tutor.Profilestatus, TutorProfileStatus.Active, StringComparison.OrdinalIgnoreCase) || tutor.Ispublic != true)
            throw new BookingException(BookingErrorCodes.TutorNotAvailable, "Gia sư chưa được duyệt hoặc chưa hiển thị công khai", 409);

        var price = await context.Tutorsubjectgradeprices
            .Include(p => p.Subject)
            .Include(p => p.Gradelevel)
            .FirstOrDefaultAsync(p => p.Id == dto.TutorSubjectGradePriceId && p.Tutorid == dto.TutorId && p.Isactive)
            ?? throw new BookingException(BookingErrorCodes.TutorNotTeachSubject, "Gia sư không dạy môn/lớp này", 409);

        var package = await context.Tutorpackages
            .Include(c => c.Tutorpackagefixedslots)
            .FirstOrDefaultAsync(c => c.Packageid == dto.PackageId && c.Tutorid == dto.TutorId && c.Isactive)
            ?? throw new BookingException(BookingErrorCodes.InvalidInput, "Package không hợp lệ", 400);

        var totalSessions = ResolveTotalSessions(dto, package);
        var lessonSlots = package.Packagetype == Tutorpackage.FixedPackageType
            ? GenerateFixedPackageSlots(package, dto.StartDate, totalSessions)
            : GenerateFlexibleSlots(dto, price.Durationminutespersession, totalSessions);

        await ValidateSlotsAsync(dto.TutorId, lessonSlots);

        var totalAmount = Math.Round(price.Priceperhour * price.Durationminutespersession / 60m * totalSessions, 2);
        int? promotionId = null;
        var discountApplied = 0m;
        if (!string.IsNullOrWhiteSpace(dto.PromotionCode))
        {
            var promoResult = await ResolvePromotionAsync(dto.PromotionCode, totalAmount);
            promotionId = promoResult.PromotionId;
            discountApplied = promoResult.DiscountAmount;
            if (promotionId.HasValue)
            {
                var promo = await context.Promotions.FirstAsync(p => p.Promotionid == promotionId.Value);
                promo.Usagecount = (promo.Usagecount ?? 0) + 1;
            }
        }

        var fees = BookingFeeCalculator.Calculate(totalAmount - discountApplied);
        string paymentCode;
        do { paymentCode = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(); }
        while (await bookingRepo.PaymentCodeExistsAsync(paymentCode));

        var booking = new Booking
        {
            Parentid = userRole == UserRole.Parent ? userId : student.Parentid,
            Studentid = student.Studentid,
            Tutorid = dto.TutorId,
            Tutorsubjectgradepriceid = price.Id,
            Packageid = package.Packageid,
            Promotionid = promotionId,
            Totalsessions = totalSessions,
            Sessionsremaining = totalSessions,
            Priceperhour = price.Priceperhour,
            Totalamount = totalAmount,
            Currency = price.Currency,
            Startdate = lessonSlots.Min(s => s.Start),
            Discountapplied = discountApplied,
            Finalprice = fees.FinalPrice,
            Platformfee = fees.PlatformFee,
            Parentfee = fees.ParentFee,
            Tutorfee = fees.TutorReceivable,
            Status = BookingStatus.PendingTutor,
            Paymentstatus = PaymentStatus.Pending,
            Paymentcode = paymentCode,
            Locationcity = dto.LocationCity,
            Locationdistrict = dto.LocationDistrict,
            Locationward = dto.LocationWard,
            Locationdetail = dto.LocationDetail,
            Createdat = VietnamTimeHelper.Now,
            Responsedeadline = VietnamTimeHelper.Now.AddHours(24)
        };

        await using var tx = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            context.Bookings.Add(booking);
            await context.SaveChangesAsync();

            var lessonPrice = Math.Round(totalAmount / totalSessions, 2);
            foreach (var slot in lessonSlots)
            {
                context.Lessons.Add(new Lesson
                {
                    Bookingid = booking.Bookingid,
                    Tutorid = dto.TutorId,
                    Studentid = student.Studentid,
                    Scheduledstart = slot.Start,
                    Scheduledend = slot.End,
                    Lessonprice = lessonPrice,
                    Status = Reserved,
                    Createdat = VietnamTimeHelper.Now
                });
            }

            context.Notifications.Add(NotificationHelper.CreateBookingNotification(dto.TutorId, "Yêu cầu đặt lịch mới", $"Bạn có yêu cầu đặt lịch mới. Mã thanh toán: {paymentCode}", booking.Bookingid));
            await context.SaveChangesAsync();
            await tx.CommitAsync();
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }

        booking.Tutor = tutor;
        booking.Student = student;
        booking.Tutorsubjectgradeprice = price;
        booking.Package = package;
        return MapToResponse(booking, student, tutor, price.Subject);
    }

    public async Task<PagedList<BookingResponse>> GetMyBookingsAsync(string userId, string userRole, int page, int pageSize, string? status = null)
    {
        try
        {
            if (userRole == UserRole.Parent)
            {
                var (items, total) = await bookingRepo.GetByParentIdPagedAsync(userId, page, pageSize, status);
                var dtos = items.Select(b => MapToResponse(b, b.Student, b.Tutor, b.Tutorsubjectgradeprice?.Subject)).ToList();
                return new PagedList<BookingResponse>(dtos, total, page, pageSize);
            }
            else
            {
                var studentIds = await context.Studentprofiles
                    .Where(s => s.Studentid == userId || s.Linkeduserid == userId)
                    .Select(s => s.Studentid)
                    .ToListAsync();

                if (studentIds.Count == 0)
                    return new PagedList<BookingResponse>(new List<BookingResponse>(), 0, page, pageSize);

                var (items, total) = await bookingRepo.GetByStudentIdsPagedAsync(studentIds, page, pageSize, status);
                var dtos = items.Select(b => MapToResponse(b, b.Student, b.Tutor, b.Tutorsubjectgradeprice?.Subject)).ToList();
                return new PagedList<BookingResponse>(dtos, total, page, pageSize);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CRITICAL] Error in GetMyBookingsAsync: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
            if (ex.InnerException != null)
                Console.WriteLine($"[CRITICAL] Inner Exception: {ex.InnerException.Message}");
            throw;
        }
    }

    public async Task<PagedList<BookingResponse>> GetTutorBookingRequestsAsync(string tutorId, int page, int pageSize, string? status = null)
    {
        var (items, total) = await bookingRepo.GetByTutorIdPagedAsync(tutorId, page, pageSize, status);
        var dtos = items.Select(b => MapToResponse(b, b.Student, b.Tutor, b.Tutorsubjectgradeprice?.Subject)).ToList();
        return new PagedList<BookingResponse>(dtos, total, page, pageSize);
    }

    public async Task<BookingResponse?> GetBookingByIdAsync(int id, string userId, string userRole)
    {
        var b = await bookingRepo.FindWithRelationsAsync(id);
        if (b == null) return null;
        if (userRole == UserRole.Parent && b.Parentid != userId) return null;
        if (userRole == UserRole.Student && b.Studentid != userId && b.Student?.Linkeduserid != userId) return null;
        if (userRole == UserRole.Tutor && b.Tutorid != userId) return null;
        return MapToResponse(b, b.Student, b.Tutor, b.Tutorsubjectgradeprice?.Subject);
    }

    public async Task<bool> CancelBookingAsync(int bookingId, string userId, string? reason = null)
    {
        var booking = await context.Bookings
            .Include(b => b.Student)
            .Include(b => b.Lessons)
            .FirstOrDefaultAsync(b => b.Bookingid == bookingId &&
                (b.Parentid == userId || b.Studentid == userId || b.Student.Linkeduserid == userId || b.Tutorid == userId));
        if (booking == null) return false;
        if (booking.Status != BookingStatus.PendingTutor &&
            booking.Status != BookingStatus.Accepted &&
            booking.Status != BookingStatus.PendingPayment &&
            booking.Status != BookingStatus.DepositPaid &&
            booking.Status != BookingStatus.PendingRemainingPayment &&
            booking.Status != BookingStatus.Ongoing &&
            booking.Status != BookingStatus.Paid)
            return false;

        var needsRefund = booking.Paymentstatus == DepositEscrowed
            || booking.Paymentstatus == Escrowed
            || booking.Paymentstatus == Paid;

        if (needsRefund)
        {
            decimal refundAmount = booking.Paymentstatus == DepositEscrowed
                ? booking.Depositamount ?? 0
                : booking.Finalprice ?? booking.Totalamount ?? 0;

            var tutorFee = booking.Tutorfee ?? 0;
            decimal tutorEscrowAmount = booking.Paymentstatus == DepositEscrowed
                ? Math.Round(tutorFee * 0.5m, 2)
                : tutorFee;

            await using var tx = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            try
            {
                if (!string.IsNullOrWhiteSpace(booking.Parentid) && refundAmount > 0)
                {
                    var parentWallet = await context.Wallets
                .FromSqlRaw(SqlQueries.LockWalletByUserId, booking.Parentid)
                        .FirstOrDefaultAsync();

                    if (parentWallet != null)
                    {
                        parentWallet.Balance = (parentWallet.Balance ?? 0) + refundAmount;
                        parentWallet.Lastupdated = VietnamTimeHelper.UtcNow;

                        context.Wallettransactions.Add(new Wallettransaction
                        {
                            Wallet = parentWallet,
                            Amount = refundAmount,
                            Transactiontype = TransactionType.Refund,
                            Referencetable = ReferenceTable.Booking,
                            Referenceid = bookingId,
                            Description = $"Hoàn tiền booking #{bookingId}",
                            Createdat = VietnamTimeHelper.UtcNow
                        });
                    }
                }

                if (!string.IsNullOrWhiteSpace(booking.Tutorid) && tutorEscrowAmount > 0)
                {
                    var tutorWallet = await context.Wallets
                .FromSqlRaw(SqlQueries.LockWalletByUserId, booking.Tutorid)
                        .FirstOrDefaultAsync();

                    if (tutorWallet != null)
                    {
                        tutorWallet.Frozenbalance = Math.Max(0, (tutorWallet.Frozenbalance ?? 0) - tutorEscrowAmount);
                        tutorWallet.Lastupdated = VietnamTimeHelper.UtcNow;

                        context.Wallettransactions.Add(new Wallettransaction
                        {
                            Wallet = tutorWallet,
                            Amount = -tutorEscrowAmount,
                            Transactiontype = TransactionType.EscrowRelease,
                            Referencetable = ReferenceTable.Booking,
                            Referenceid = bookingId,
                            Description = $"Giải phóng escrow booking #{bookingId} do hủy",
                            Createdat = VietnamTimeHelper.UtcNow
                        });
                    }
                }

                booking.Status = BookingStatus.Cancelled;
                booking.Cancellationreason = reason;
                booking.Cancelledby = userId;
                booking.Cancelledat = VietnamTimeHelper.UtcNow;
                booking.Updatedat = VietnamTimeHelper.UtcNow;
                booking.Refundstatus = RefundStatus.Refunded;
                booking.Refundamount = refundAmount;

                foreach (var l in booking.Lessons.Where(x => x.Status is Scheduled or Reserved))
                    l.Status = Cancelled;

                await context.SaveChangesAsync();
                await tx.CommitAsync();

                if (!string.IsNullOrWhiteSpace(booking.Parentid) && refundAmount > 0)
                {
                    await notificationService.CreateNotificationAsync(new MV.DomainLayer.DTO.RequestModel.NotificationRequest
                    {
                        Userid = booking.Parentid,
                        Title = "Hoàn tiền thành công",
                        Message = $"Booking #{bookingId} đã được hủy. Số tiền {refundAmount:N0}đ đã được hoàn vào ví của bạn."
                    });
                }
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                logger.LogError(ex, "Lỗi khi hoàn tiền booking {BookingId}", bookingId);
                booking.Refundstatus = RefundStatus.RefundFailed;
                await context.SaveChangesAsync();
            }
        }
        else
        {
            booking.Refundstatus = RefundStatus.NoRefund;
            booking.Status = BookingStatus.Cancelled;
            booking.Cancellationreason = reason;
            booking.Cancelledby = userId;
            booking.Cancelledat = VietnamTimeHelper.UtcNow;
            booking.Updatedat = VietnamTimeHelper.UtcNow;

            foreach (var l in booking.Lessons.Where(x => x.Status is Scheduled or Reserved))
                l.Status = Cancelled;

            await context.SaveChangesAsync();
        }

        return true;
    }

    public async Task<List<ScheduleItemResponse>> GetTutorBookedSlotsAsync(string tutorId, DateTime startDate)
    {
        var fromUtc = VietnamTimeHelper.ToUtc(startDate);
        var toUtc = fromUtc.AddDays(WeeksPerMonth * 7);

        var lessons = await context.Lessons
            .Where(l => l.Tutorid == tutorId
                && l.Scheduledstart >= fromUtc
                && l.Scheduledstart < toUtc
                && l.Status != Cancelled
                && l.Status != CancelledNoshow
                && l.Status != Completed
                && l.Status != NoShow)
            .OrderBy(l => l.Scheduledstart)
            .ToListAsync();

        return lessons
            .Select(l =>
            {
                var startVn = VietnamTimeHelper.ToVietnamTime(l.Scheduledstart);
                var endVn = VietnamTimeHelper.ToVietnamTime(l.Scheduledend);
                return new ScheduleItemResponse
                {
                    DayOfWeek = (int)startVn.DayOfWeek,
                    StartTime = startVn.ToString("HH:mm"),
                    EndTime = endVn.ToString("HH:mm")
                };
            })
            .DistinctBy(s => $"{s.DayOfWeek}|{s.StartTime}|{s.EndTime}")
            .ToList();
    }

    private static string GetDayOfWeekVn(int dow) => dow switch
    {
        0 => "Chủ nhật",
        1 => "Thứ Hai",
        2 => "Thứ Ba",
        3 => "Thứ Tư",
        4 => "Thứ Năm",
        5 => "Thứ Sáu",
        6 => "Thứ Bảy",
        _ => $"ngày {dow}"
    };

    private async Task ValidateSlotsAsync(string tutorId, IReadOnlyList<LessonSlot> lessonSlots)
    {
        if (lessonSlots.Count == 0)
            throw new BookingException(BookingErrorCodes.InvalidSchedule, "Không tìm thấy lịch học hợp lệ", 400);

        var tutorAvailabilities = await context.Tutoravailabilities
            .Where(a => a.Tutorid == tutorId)
            .ToListAsync();

        foreach (var slot in lessonSlots)
        {
            var startVn = VietnamTimeHelper.ToVietnamTime(slot.Start);
            var endVn = VietnamTimeHelper.ToVietnamTime(slot.End);
            var startTime = TimeOnly.FromTimeSpan(startVn.TimeOfDay);
            var endTime = TimeOnly.FromTimeSpan(endVn.TimeOfDay);
            var bookingDate = DateOnly.FromDateTime(startVn);

            var covered = tutorAvailabilities.Any(a =>
            {
                if (!a.Starttime.HasValue || !a.Endtime.HasValue || !a.Dayofweek.HasValue) return false;

                var validFrom = DateOnly.FromDateTime(VietnamTimeHelper.ToVietnamTime(a.Createdat ?? VietnamTimeHelper.Now));
                var validTo = validFrom.AddDays(AvailabilityValidDays);

                return a.Dayofweek.Value == (int)startVn.DayOfWeek
                    && a.Starttime.Value <= startTime
                    && a.Endtime.Value >= endTime
                    && bookingDate >= validFrom
                    && bookingDate <= validTo;
            });

            if (!covered)
                throw new BookingException(BookingErrorCodes.ScheduleNotInAvailability,
                    $"Slot {startVn:dd/MM/yyyy HH:mm}-{endVn:HH:mm} nằm ngoài lịch rảnh của gia sư", 400);

            var hasConflict = await context.Lessons.AnyAsync(l =>
                l.Tutorid == tutorId
                && l.Status != Cancelled
                && l.Status != CancelledNoshow
                && l.Status != Completed
                && l.Status != NoShow
                && l.Scheduledstart < slot.End
                && l.Scheduledend > slot.Start);

            if (hasConflict)
                throw new BookingException(BookingErrorCodes.ScheduleConflict,
                    $"Gia sư đã có lịch dạy vào {startVn:HH:mm ngày dd/MM/yyyy}. Vui lòng chọn khung giờ khác.", 409);
        }
    }

    private static int ResolveTotalSessions(CreateBookingRequest dto, Tutorpackage package)
    {
        if (package.Packagetype == Tutorpackage.FlexiblePackageType)
        {
            var count = dto.FlexibleSlots?.Count ?? 0;
            if (count <= 0)
                throw new BookingException(BookingErrorCodes.InvalidSchedule, "Package flexible yêu cầu chọn ít nhất một buổi học", 400);
            if (dto.TotalSessions.HasValue && dto.TotalSessions.Value != count)
                throw new BookingException(BookingErrorCodes.InvalidSchedule, "TotalSessions phải bằng số lượng flexibleSlots", 400);
            return count;
        }

        if (!dto.TotalSessions.HasValue || dto.TotalSessions.Value <= 0)
            throw new BookingException(BookingErrorCodes.InvalidSchedule, "Package fixed yêu cầu TotalSessions lớn hơn 0", 400);

        return dto.TotalSessions.Value;
    }

    private static List<LessonSlot> GenerateFixedPackageSlots(Tutorpackage package, DateTime startDate, int totalSessions)
    {
        if (package.Tutorpackagefixedslots.Count == 0)
            throw new BookingException(BookingErrorCodes.InvalidSchedule, "Package cố định chưa có khung giờ", 400);

        var startDateVn = VietnamTimeHelper.ToVietnamTime(VietnamTimeHelper.ToUtc(startDate)).Date;
        var todayVn = VietnamTimeHelper.ToVietnamTime(VietnamTimeHelper.Now).Date;
        var currentDate = startDateVn >= todayVn ? startDateVn : todayVn;
        var fixedSlots = package.Tutorpackagefixedslots
            .OrderBy(s => s.Dayofweek)
            .ThenBy(s => s.Starttime)
            .ToList();
        var result = new List<LessonSlot>();

        while (result.Count < totalSessions)
        {
            foreach (var fixedSlot in fixedSlots.Where(s => s.Dayofweek == (int)currentDate.DayOfWeek))
            {
                if (result.Count >= totalSessions) break;

                var start = new DateTime(currentDate.Year, currentDate.Month, currentDate.Day,
                    fixedSlot.Starttime.Hour, fixedSlot.Starttime.Minute, 0, DateTimeKind.Unspecified);
                var end = new DateTime(currentDate.Year, currentDate.Month, currentDate.Day,
                    fixedSlot.Endtime.Hour, fixedSlot.Endtime.Minute, 0, DateTimeKind.Unspecified);
                result.Add(new LessonSlot(
                    TimeZoneInfo.ConvertTimeToUtc(start, VietnamTimeHelper.Tz),
                    TimeZoneInfo.ConvertTimeToUtc(end, VietnamTimeHelper.Tz)));
            }

            currentDate = currentDate.AddDays(1);
        }

        return result;
    }

    private static List<LessonSlot> GenerateFlexibleSlots(CreateBookingRequest dto, int durationMinutes, int totalSessions)
    {
        var slots = dto.FlexibleSlots ?? [];
        if (slots.Count != totalSessions)
            throw new BookingException(BookingErrorCodes.InvalidSchedule,
                $"Package linh hoạt yêu cầu chọn đúng {totalSessions} buổi học", 400);

        var duration = TimeSpan.FromMinutes(durationMinutes);
        return slots
            .Select(s =>
            {
                var start = VietnamTimeHelper.ToUtc(s.ScheduledStart);
                var end = VietnamTimeHelper.ToUtc(s.ScheduledEnd);
                if (end <= start || Math.Abs((end - start - duration).TotalMinutes) > 1)
                    throw new BookingException(BookingErrorCodes.InvalidSchedule,
                        $"Mỗi buổi học phải kéo dài {durationMinutes} phút", 400);
                return new LessonSlot(start, end);
            })
            .OrderBy(s => s.Start)
            .ToList();
    }

    private static BookingResponse MapToResponse(Booking b,
        Studentprofile? student, Tutorprofile? tutor, Subject? subject)
    {
        var grade = b.Tutorsubjectgradeprice?.Gradelevel ?? student?.GradelevelNavigation;
        var lessons = b.Lessons?
            .OrderBy(l => l.Scheduledstart)
            .Select((l, i) => new BookingLessonSlotResponse
            {
                LessonId = l.Lessonid,
                SessionIndex = i + 1,
                ScheduledStart = VietnamTimeHelper.ToVietnamTime(l.Scheduledstart),
                ScheduledEnd = VietnamTimeHelper.ToVietnamTime(l.Scheduledend),
                Status = l.Status,
                LessonPrice = l.Lessonprice
            })
            .ToList();

        return new BookingResponse
        {
            BookingId = b.Bookingid,
            ParentId = b.Parentid,
            Student = student == null ? null : new StudentMiniResponse
            {
                StudentId = student.Studentid,
                // Ưu tiên Fullname từ profile; nếu null thì fallback sang Linkeduser.Fullname (student tự đăng ký)
                FullName = student.Fullname ?? student.Linkeduser?.Fullname,
                GradeLevelId = student.Gradelevelid,
                GradeLevel = student.Gradelevel,
                GradeLevelName = student.Gradelevel
            },
            Tutor = tutor == null ? null : new TutorMiniResponse
            {
                TutorId = tutor.Tutorid,
                FullName = tutor.Tutor?.Fullname,
                AvatarUrl = tutor.Tutor?.Avatarurl,
                HourlyRate = b.Priceperhour
            },
            Subject = subject == null ? null : new SubjectResponse
            {
                SubjectId = subject.Subjectid,
                SubjectName = subject.Subjectname
            },
            Package = b.Package == null ? null : new BookingPackageResponse
            {
                PackageId = b.Package.Packageid,
                Name = b.Package.Name,
                PackageType = b.Package.Packagetype
            },
            TutorSubjectGradePriceId = b.Tutorsubjectgradepriceid,
            GradeLevel = grade == null ? null : new GradeLevelResponse
            {
                GradeLevelId = grade.Gradelevelid,
                GradeName = grade.Gradename,
                LevelOrder = grade.Levelorder
            },
            PackageType = b.Package?.Packagetype,
            SessionCount = b.Totalsessions ?? 0,
            TotalSessions = b.Totalsessions,
            DurationMinutesPerSession = b.Tutorsubjectgradeprice?.Durationminutespersession ?? 60,
            Price = b.Totalamount,
            PricePerHour = b.Priceperhour,
            TotalAmount = b.Totalamount,
            Currency = b.Currency,
            DiscountApplied = b.Discountapplied,
            FinalPrice = b.Finalprice,
            PlatformFee = b.Platformfee,
            Status = b.Status,
            PaymentStatus = b.Paymentstatus,
            PaymentCode = b.Paymentcode,
            Schedule = lessons?.Select(l => new ScheduleItemResponse
            {
                DayOfWeek = (int)l.ScheduledStart.DayOfWeek,
                StartTime = l.ScheduledStart.ToString("HH:mm"),
                EndTime = l.ScheduledEnd.ToString("HH:mm")
            }).ToList(),
            Lessons = lessons,
            StartDate = b.Startdate,
            // BE luôn lưu UTC (DateTime.UtcNow), ToVietnamTime() convert sang UTC+7 khi trả về FE.
            // Pattern này đúng cả local lẫn cloud deployment.
            CreatedAt = b.Createdat.HasValue ? VietnamTimeHelper.ToVietnamTime(b.Createdat.Value) : (DateTime?)null,
            PaymentDueAt = b.Paymentdueat.HasValue ? VietnamTimeHelper.ToVietnamTime(b.Paymentdueat.Value) : (DateTime?)null,
            DepositAmount = b.Depositamount,
            RemainingAmount = b.Remainingamount,
            DepositPaidAt = b.Depositpaidat.HasValue ? VietnamTimeHelper.ToVietnamTime(b.Depositpaidat.Value) : (DateTime?)null,
            RemainingPaidAt = b.Remainingpaidat.HasValue ? VietnamTimeHelper.ToVietnamTime(b.Remainingpaidat.Value) : (DateTime?)null,
            EscrowStatus = b.Escrowstatus,
            RefundAmount = b.Refundamount,
            RefundStatus = b.Refundstatus,
            CancellationReason = b.Cancellationreason,
            CancelledBy = b.Cancelledby,
            CancelledAt = b.Cancelledat.HasValue ? VietnamTimeHelper.ToVietnamTime(b.Cancelledat.Value) : (DateTime?)null
        };
    }

    private sealed record LessonSlot(DateTime Start, DateTime End);

}
