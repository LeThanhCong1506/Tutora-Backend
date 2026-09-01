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
using static MV.DomainLayer.Constants.ClassSessionStatus;
using static MV.DomainLayer.Constants.PaymentStatus;

namespace MV.ApplicationLayer.Services;

public partial class BookingService(
    IBookingRepository bookingRepo,
    IStudentRepository studentRepo,
    ITutorRepository tutorRepo,
    IAppDbContext context,          // retained only for: ClassSessions (conflict check), Subjects, Tutorsubjects, Tutoravailabilities, Promotions, Wallets, Wallettransactions, Notifications
    INotificationService notificationService,
    IChatService chatService,
    ISettlementService settlementService,
    IAiCreditService aiCreditService,
    ILargeTransactionOtpService largeTransactionOtpService,
    ICommissionConfigService commissionConfigService,
    ILogger<BookingService> logger) : IBookingService
{
    private const int AvailabilityValidDays = 30;

    public async Task<BookingResponse> CreateBookingAsync(string userId, string userRole, CreateBookingRequest dto)
    {
        // Normalize StartDate: nếu Kind là Utc thì convert sang user time để so sánh ngày
        // Nếu Unspecified thì coi như đã là user time
        var startDateLocal = dto.StartDate.Kind == DateTimeKind.Utc 
            ? dto.StartDate
            : dto.StartDate;
            
        if (startDateLocal.Date < TimeZoneHelper.UtcNow.Date)
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

        // Tài khoản học sinh do phụ huynh quản lý không tự đặt lịch được — chỉ phụ huynh mới có
        // quyền đặt lịch cho con (xem StudentService.Identity.GetBookingEligibilityAsync — cùng luật).
        if (userRole == UserRole.Student && !string.IsNullOrWhiteSpace(student.Parentid))
            throw new BookingException(BookingErrorCodes.StudentManagedByParent,
                "Tài khoản học sinh do phụ huynh quản lý không thể tự đặt lịch. Vui lòng nhờ phụ huynh đặt lịch giúp.", 403);

        // Học sinh TỰ đăng ký (không có phụ huynh) thì tự trả nên vẫn phải xác minh tuổi — không
        // có ai đứng ra chịu trách nhiệm thay.
        if (userRole == UserRole.Student)
        {
            var studentUser = await context.Users.FirstOrDefaultAsync(u => u.Userid == userId)
                ?? throw new BookingException(BookingErrorCodes.NotStudentOwner, "Không tìm thấy tài khoản học sinh", 404);

            if (studentUser.Isidentityverified != true)
                throw new BookingException(BookingErrorCodes.StudentIdentityNotVerified,
                    "Bạn cần xác minh độ tuổi để có thể đặt lịch học", 403);

            if (!AgeHelper.IsOldEnoughToSelfBook(studentUser.Birthdate))
                throw new BookingException(BookingErrorCodes.StudentUnderage,
                    $"Bạn phải đủ {AgeHelper.MinSelfBookingAge} tuổi mới có thể đặt lịch học", 403);
        }

        var tutor = await context.Tutorprofiles.Include(t => t.Tutor).FirstOrDefaultAsync(t => t.Tutorid == dto.TutorId)
            ?? throw new BookingException(BookingErrorCodes.TutorNotFound, "Không tìm thấy gia sư", 404);
        // Ispublic đã bị các luồng suspend/deactivate hiện có (tự khóa, admin khóa, auto-suspend
        // do cảnh cáo) tắt kèm theo, nhưng đó là suy luận gián tiếp qua một cờ hiển thị marketplace —
        // kiểm thẳng Status của chính tài khoản tutor (cùng cờ mà OnTokenValidated ở Program.cs dùng
        // để chặn đăng nhập) để không phụ thuộc mọi nơi ghi suspension đều nhớ đồng bộ Ispublic.
        if (tutor.Tutor?.Status == 0)
            throw new BookingException(BookingErrorCodes.TutorNotAvailable, "Tài khoản gia sư đã bị khóa hoặc tạm ngưng", 409);
        if (!string.Equals(tutor.Profilestatus, TutorProfileStatus.Active, StringComparison.OrdinalIgnoreCase) || tutor.Ispublic != true)
            throw new BookingException(BookingErrorCodes.TutorNotAvailable, "Gia sư chưa được duyệt hoặc chưa hiển thị công khai", 409);
        if (!tutor.Isacceptingbookings)
            throw new BookingException(BookingErrorCodes.TutorNotAvailable, "Gia sư hiện đang tạm dừng nhận booking mới.", 409);

        var price = await context.Tutorsubjectgradeprices
            .Include(p => p.Subject)
            .Include(p => p.Gradelevel)
            .FirstOrDefaultAsync(p => p.Id == dto.TutorSubjectGradePriceId && p.Tutorid == dto.TutorId && p.Isactive)
            ?? throw new BookingException(BookingErrorCodes.TutorNotTeachSubject, "Gia sư không dạy môn/lớp này", 409);

        if (price.Subject?.IsActive != true || price.Gradelevel?.IsActive != true)
            throw new BookingException(BookingErrorCodes.SubjectOrGradeLevelInactive,
                "Môn học/khối lớp này đã ngừng cung cấp trên hệ thống", 409);

        var package = await context.Tutorpackages
            .Include(c => c.Tutorpackagefixedslots)
            .FirstOrDefaultAsync(c => c.Packageid == dto.PackageId && c.Tutorid == dto.TutorId && c.Isactive)
            ?? throw new BookingException(BookingErrorCodes.InvalidInput, "Package không hợp lệ", 400);

        // Gói cố định là lịch được tutor tạo riêng cho một môn. Không cho phép lấy lịch
        // của gói Toán để tạo booking có subject-grade price của Vật lý (hoặc ngược lại).
        if (package.Packagetype == Tutorpackage.FixedPackageType && package.Subjectid != price.Subjectid)
            throw new BookingException(BookingErrorCodes.InvalidInput,
                "Gói học không thuộc môn học đã chọn", 409);

        var totalSessions = ResolveTotalSessions(dto, package);
        var classSessionSlots = package.Packagetype == Tutorpackage.FixedPackageType
            ? GenerateFixedPackageSlots(package, dto.StartDate, totalSessions)
            : GenerateFlexibleSlots(dto, price.Durationminutespersession, totalSessions);

        // Chỉ gói LINH HOẠT mới cần kiểm phân bổ: gói cố định tự sinh lịch theo khung tuần của
        // chính nó nên phụ huynh không chọn gì. Xem BookingSchedulePolicy.
        if (package.Packagetype != Tutorpackage.FixedPackageType)
        {
            try
            {
                BookingSchedulePolicy.EnsureValidDistribution(
                    classSessionSlots.Select(x => x.Start).ToList(),
                    price.Sessionsperweek);
            }
            catch (InvalidOperationException ex)
            {
                throw new BookingException(BookingErrorCodes.InvalidSchedule, ex.Message, 400);
            }
        }

        await ValidateSlotsAsync(dto.TutorId, classSessionSlots);

        // Tính theo TỔNG SỐ GIỜ THỰC của các slot đã sinh ra (classSessionSlots), không phải
        // price.Durationminutespersession × totalSessions. Với gói cố định, mỗi buổi lấy giờ thật
        // từ Tutorpackagefixedslots của chính gói đó — có thể khác con số tham chiếu ở
        // Tutorsubjectgradeprice (vd gói "cao cấp" 3h/buổi trong khi giá gốc ghi 1h/buổi). Với booking
        // linh hoạt (không qua gói cố định), slot vẫn được sinh đúng bằng price.Durationminutespersession
        // (xem GenerateFlexibleSlots) nên công thức này cho kết quả giống hệt cách tính cũ ở nhánh đó.
        var totalHours = classSessionSlots.Sum(slot => (decimal)(slot.End - slot.Start).TotalHours);
        var totalAmount = Math.Round(price.Priceperhour * totalHours, 2);
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

        var (parentFeePct, tutorFeePct) = await commissionConfigService.GetFeePercentsAsync();
        var fees = BookingFeeCalculator.Calculate(totalAmount - discountApplied, parentFeePct, tutorFeePct);
        var (depositAmount, remainingAmount) = BookingFeeCalculator.CalculatePaymentPhases(
            fees.FinalPrice, totalSessions);

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
            Startdate = classSessionSlots.Min(s => s.Start),
            Discountapplied = discountApplied,
            Finalprice = fees.FinalPrice,
            Platformfee = fees.PlatformFee,
            Parentfee = fees.ParentFee,
            Tutorfee = fees.TutorReceivable,
            Depositamount = depositAmount,
            Remainingamount = remainingAmount,
            Status = BookingStatus.PendingPayment,
            Paymentstatus = PaymentStatus.Pending,
            // Người đặt có 10 phút để trả buổi đầu (vừa bấm đặt là đi trả luôn).
            Paymentdueat = TimeZoneHelper.UtcNow.AddMinutes(10),
            Createdbyrole = userRole,
            Locationcity = dto.LocationCity,
            Locationdistrict = dto.LocationDistrict,
            Locationward = dto.LocationWard,
            Locationdetail = dto.LocationDetail,
            Createdat = TimeZoneHelper.UtcNow
        };

        await using var tx = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            context.Bookings.Add(booking);
            await context.SaveChangesAsync();

            // Giá từng buổi theo ĐÚNG thời lượng thật của buổi đó (không chia đều tổng tiền cho số
            // buổi) — cho kết quả giống hệt cách chia đều cũ khi mọi buổi cùng thời lượng, nhưng vẫn
            // đúng nếu sau này 1 gói có các buổi dài ngắn khác nhau.
            foreach (var slot in classSessionSlots)
            {
                var slotPrice = Math.Round(price.Priceperhour * (decimal)(slot.End - slot.Start).TotalHours, 2);
                context.ClassSessions.Add(new ClassSession
                {
                    Bookingid = booking.Bookingid,
                    Tutorid = dto.TutorId,
                    Studentid = student.Studentid,
                    Scheduledstart = slot.Start,
                    Scheduledend = slot.End,
                    Lessonprice = slotPrice,
                    Status = Reserved,
                    Createdat = TimeZoneHelper.UtcNow
                });
            }

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

        return MapToResponse(booking, student, tutor, price.Subject, parentFeePct, tutorFeePct);
    }

    public async Task<PagedList<BookingResponse>> GetMyBookingsAsync(string userId, string userRole, int page, int pageSize, string? status = null)
    {
        try
        {
            var (parentFeePct, tutorFeePct) = await commissionConfigService.GetFeePercentsAsync();
            if (userRole == UserRole.Parent)
            {
                var (items, total) = await bookingRepo.GetByParentIdPagedAsync(userId, page, pageSize, status);
                var dtos = items.Select(b => MapToResponse(b, b.Student, b.Tutor, b.Tutorsubjectgradeprice?.Subject, parentFeePct, tutorFeePct)).ToList();
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
                var dtos = items.Select(b => MapToResponse(b, b.Student, b.Tutor, b.Tutorsubjectgradeprice?.Subject, parentFeePct, tutorFeePct)).ToList();
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

    /// <summary>Booking đã đóng hẳn — Home của phụ huynh không hiện các trạng thái này.</summary>
    private static readonly string[] ClosedBookingStatuses =
    {
        BookingStatus.Cancelled,
        BookingStatus.CancelledNoshow,
        BookingStatus.PaymentTimeout,
    };

    public async Task<PagedList<BookingResponse>> GetChildBookingsAsync(
        string userId, string userRole, string studentId, int page, int pageSize,
        string? status = null, bool excludeClosed = false)
    {
        var ownedIds = userRole == UserRole.Parent
            ? await context.Studentprofiles.Where(s => s.Parentid == userId).Select(s => s.Studentid).ToListAsync()
            : await context.Studentprofiles.Where(s => s.Studentid == userId || s.Linkeduserid == userId).Select(s => s.Studentid).ToListAsync();

        if (!ownedIds.Contains(studentId))
            throw new UnauthorizedAccessException("Bạn không có quyền xem lớp học của học sinh này.");

        var (parentFeePct, tutorFeePct) = await commissionConfigService.GetFeePercentsAsync();

        // excludeClosed lọc TRƯỚC khi phân trang, nên totalCount cũng đúng.
        if (excludeClosed && string.IsNullOrWhiteSpace(status))
        {
            var q = context.Bookings
                .AsNoTracking()
                .Include(b => b.Student).ThenInclude(s => s!.GradelevelNavigation)
                .Include(b => b.Tutor).ThenInclude(t => t!.Tutor)
                .Include(b => b.Tutorsubjectgradeprice).ThenInclude(p => p!.Subject)
                .Include(b => b.Tutorsubjectgradeprice).ThenInclude(p => p!.Gradelevel)
                .Include(b => b.Package)
                .Include(b => b.ClassSessions)
                .Where(b => b.Studentid == studentId
                         && (b.Status == null || !ClosedBookingStatuses.Contains(b.Status)));

            var closedTotal = await q.CountAsync();
            var closedItems = await q
                .OrderByDescending(b => b.Createdat)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return new PagedList<BookingResponse>(
                closedItems.Select(b => MapToResponse(b, b.Student, b.Tutor, b.Tutorsubjectgradeprice?.Subject, parentFeePct, tutorFeePct)).ToList(),
                closedTotal, page, pageSize);
        }

        var (items, total) = await bookingRepo.GetByStudentIdsPagedAsync(
            new List<string> { studentId }, page, pageSize, status);
        var dtos = items.Select(b => MapToResponse(b, b.Student, b.Tutor, b.Tutorsubjectgradeprice?.Subject, parentFeePct, tutorFeePct)).ToList();
        return new PagedList<BookingResponse>(dtos, total, page, pageSize);
    }

    public async Task<PagedList<BookingResponse>> GetTutorBookingRequestsAsync(string tutorId, int page, int pageSize, string? status = null)
    {
        var (parentFeePct, tutorFeePct) = await commissionConfigService.GetFeePercentsAsync();
        var (items, total) = await bookingRepo.GetByTutorIdPagedAsync(tutorId, page, pageSize, status);
        var dtos = items.Select(b => MapToResponse(b, b.Student, b.Tutor, b.Tutorsubjectgradeprice?.Subject, parentFeePct, tutorFeePct)).ToList();
        return new PagedList<BookingResponse>(dtos, total, page, pageSize);
    }

    public async Task<BookingResponse?> GetBookingByIdAsync(int id, string userId, string userRole)
    {
        var b = await bookingRepo.FindWithRelationsAsync(id);
        if (b == null) return null;
        if (userRole == UserRole.Parent && b.Parentid != userId) return null;
        if (userRole == UserRole.Student && b.Studentid != userId && b.Student?.Linkeduserid != userId) return null;
        if (userRole == UserRole.Tutor && b.Tutorid != userId) return null;
        var (parentFeePct, tutorFeePct) = await commissionConfigService.GetFeePercentsAsync();
        return MapToResponse(b, b.Student, b.Tutor, b.Tutorsubjectgradeprice?.Subject, parentFeePct, tutorFeePct);
    }

    public async Task<bool> CancelBookingAsync(int bookingId, string userId, string? reason = null)
    {
        Booking booking;
        var now = TimeZoneHelper.UtcNow;
        var needsRefund = false;
        var refundAmount = 0m;

        await using var tx = await context.Database.BeginTransactionAsync();
        try
        {
            var lockedBooking = await bookingRepo.FindWithRelationsForUpdateAsync(bookingId);
            if (lockedBooking == null)
            {
                await tx.CommitAsync();
                return false;
            }

            booking = lockedBooking;
            if (booking.Parentid != userId
                 && booking.Studentid != userId
                 && booking.Student?.Linkeduserid != userId
                 && booking.Tutorid != userId)
            {
                await tx.CommitAsync();
                return false;
            }

            if (booking.Status != BookingStatus.PendingTutor &&
                booking.Status != BookingStatus.Accepted &&
                booking.Status != BookingStatus.PendingPayment &&
                booking.Status != BookingStatus.DepositPaid &&
                booking.Status != BookingStatus.PendingRemainingPayment &&
                booking.Status != BookingStatus.Ongoing &&
                booking.Status != BookingStatus.Paid)
            {
                await tx.CommitAsync();
                return false;
            }

            needsRefund = booking.Paymentstatus == DepositEscrowed
                || booking.Paymentstatus == Escrowed
                || booking.Paymentstatus == Paid;

            if (needsRefund && HasStartedOrSettledLesson(booking, now))
            {
                logger.LogWarning(
                    "Rejected paid cancellation for booking {BookingId} because at least one lesson has already started, completed, settled, or entered dispute/no-show.",
                    bookingId);
                await tx.CommitAsync();
                return false;
            }

            // Buổi học thử: phụ huynh/học sinh chỉ được tự hủy (và nhận hoàn 100%) nếu còn cách giờ
            // học đầu tiên >= 2h. Trong vòng 2h, họ phải chờ hoặc báo cáo gia sư không dạy (luồng
            // no-show dispute có sẵn) — không áp dụng mốc này khi chính gia sư là người hủy.
            var isTutorCaller = !string.IsNullOrWhiteSpace(booking.Tutorid) && booking.Tutorid == userId;
            if (needsRefund && !isTutorCaller && IsWithinTrialCancelWindow(booking, now))
            {
                logger.LogWarning(
                    "Rejected parent/student cancellation for booking {BookingId}: within the {Hours}h window before the first session.",
                    bookingId, TrialCancelWindowHours);
                await tx.CommitAsync();
                return false;
            }

            if (needsRefund)
            {
                refundAmount = TutorResponseTimeoutPolicy.ParentRefundAmount(booking);

                var refundRecipientId = ResolveRefundRecipientId(booking);

                if (refundAmount <= 0 || string.IsNullOrWhiteSpace(refundRecipientId))
                    throw new InvalidOperationException($"Booking #{bookingId} has no valid refund recipient or amount.");

                var parentWallet = await WalletLockHelper.GetOrCreateForUpdateAsync(context, refundRecipientId, now);
                parentWallet.Balance = (parentWallet.Balance ?? 0) + refundAmount;
                parentWallet.Lastupdated = now;
                context.Wallettransactions.Add(new Wallettransaction
                {
                    Wallet = parentWallet,
                    Amount = refundAmount,
                    Transactiontype = TransactionType.Refund,
                    Referencetable = ReferenceTable.Booking,
                    Referenceid = bookingId,
                    Description = $"Hoàn tiền booking #{bookingId}",
                    Createdat = now
                });

                var tutorEscrowAmount = TutorResponseTimeoutPolicy.TutorEscrowAmount(booking);

                if (tutorEscrowAmount > 0)
                {
                    if (string.IsNullOrWhiteSpace(booking.Tutorid))
                        throw new InvalidOperationException($"Booking #{bookingId} has escrow but no tutor id.");

                    var tutorWallet = await WalletLockHelper.GetRequiredForUpdateAsync(context, booking.Tutorid);
                    if ((tutorWallet.Frozenbalance ?? 0) < tutorEscrowAmount)
                        throw new InvalidOperationException($"Tutor escrow balance is insufficient for booking #{bookingId}.");

                    // Rút escrow khỏi frozen (tutor không thực nhận khi booking bị hủy).
                    tutorWallet.Frozenbalance = Math.Max(0, (tutorWallet.Frozenbalance ?? 0) - tutorEscrowAmount);
                    tutorWallet.Lastupdated = TimeZoneHelper.UtcNow;

                    context.Wallettransactions.Add(new Wallettransaction
                    {
                        Wallet = tutorWallet,
                        Amount = -tutorEscrowAmount,
                        Transactiontype = TransactionType.EscrowReversal,
                        Referencetable = ReferenceTable.Booking,
                        Referenceid = bookingId,
                        Description = $"Giải phóng escrow booking #{bookingId} do hủy",
                        Createdat = TimeZoneHelper.UtcNow
                    });
                }
                }

            booking.Status = BookingStatus.Cancelled;
            booking.Cancellationreason = reason;
            booking.Cancelledby = userId;
            booking.Cancelledat = now;
            booking.Updatedat = now;
            booking.Responsedeadline = null;

            foreach (var classSession in booking.ClassSessions.Where(x => x.Status is Scheduled or Reserved))
                classSession.Status = Cancelled;

            await PromotionUsageHelper.ReturnUsageAsync(context, booking.Promotionid);
            await context.SaveChangesAsync();
            await tx.CommitAsync();
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            logger.LogError(ex, "Lỗi khi hủy/hoàn tiền booking {BookingId}", bookingId);
            throw;
        }

        var refundNotifyUserId = ResolveRefundRecipientId(booking);
        if (needsRefund && !string.IsNullOrWhiteSpace(refundNotifyUserId) && refundAmount > 0)
        {
            try
            {
                await notificationService.CreateNotificationAsync(new NotificationRequest
                {
                    Userid = refundNotifyUserId,
                    Title = "Hoàn tiền thành công",
                    Message = $"Booking #{bookingId} đã được hủy. Số tiền {refundAmount:N0}đ đã được hoàn vào ví của bạn.",
                    Type = NotificationType.PaymentRefundSuccess,
                    Referenceid = bookingId.ToString()
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Không thể gửi thông báo hoàn tiền cho booking {BookingId}", bookingId);
            }
        }

        var cancelledByTutor = !string.IsNullOrWhiteSpace(booking.Tutorid) && booking.Tutorid == userId;
        var reasonSuffix = string.IsNullOrWhiteSpace(reason) ? "" : $" Lý do: {reason}";
        var (payerId, payerIsStudent) = BookingPayerResolver.Resolve(booking);

        if (!string.IsNullOrWhiteSpace(payerId) && !string.IsNullOrWhiteSpace(booking.Tutorid))
        {
            try
            {
                var senderId = cancelledByTutor ? booking.Tutorid! : payerId!;
                var channelId = await chatService.GetOrCreateChannelAsync(payerId!, booking.Tutorid!, payerIsStudent);
                await chatService.SendMessageAsync(senderId, channelId, new ChatMessageCreateRequest
                {
                    Content = $"🚫 Đặt lịch #{bookingId} đã bị hủy.{reasonSuffix}",
                    MessageType = ChatMessageType.BookingCancelled,
                    Metadata = new { bookingId, status = booking.Status, reason }
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Không thể gửi tin nhắn hủy booking {BookingId} vào kênh chat", bookingId);
            }
        }

        var counterpartId = cancelledByTutor ? payerId : booking.Tutorid;
        if (!string.IsNullOrWhiteSpace(counterpartId))
        {
            try
            {
                await notificationService.CreateNotificationAsync(new NotificationRequest
                {
                    Userid = counterpartId,
                    Title = cancelledByTutor ? "Gia sư đã hủy đặt lịch" : "Đặt lịch đã bị hủy",
                    Message = cancelledByTutor
                        ? $"Gia sư đã hủy đặt lịch #{bookingId}.{reasonSuffix}"
                        : $"Phụ huynh/học sinh đã hủy đặt lịch #{bookingId}.{reasonSuffix}",
                    Type = NotificationType.BookingCancelled,
                    Referenceid = bookingId.ToString()
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Không thể gửi thông báo hủy booking {BookingId}", bookingId);
            }
        }

        return true;
    }

    public Task<bool> FinalizeBookingEarlyByUserAsync(
        int bookingId,
        string userId,
        string? reason = null,
        CancellationToken ct = default)
        => settlementService.FinalizeBookingEarlyByUserAsync(bookingId, userId, reason, ct);


    /// <summary>
    /// Người nhận hoàn tiền của booking = người đã trả tiền:
    /// - Phụ huynh đặt hộ → <c>Parentid</c>.
    /// - Học sinh tự đặt (Parentid null) → ví học sinh: tài khoản đăng nhập (<c>Student.Linkeduserid</c>),
    ///   fallback <c>Studentid</c> (student tự đăng ký thường có Studentid == userId).
    /// Yêu cầu booking đã được load kèm <c>Student</c> (FindWithRelationsForUpdateAsync).
    /// </summary>
    private static string? ResolveRefundRecipientId(Booking booking)
        => BookingPayerResolver.Resolve(booking).Id;

    private static bool HasStartedOrSettledLesson(Booking booking, DateTime now)
    {
        return booking.ClassSessions.Any(l =>
            l.Issettled == true ||
            (l.Scheduledstart <= now && l.Status is Scheduled or Reserved) ||
            l.Status is InProgress
                or PendingConfirmation
                or Completed
                or Disputed
                or NoShow
                or CancelledNoshow);
    }

    /// <summary>Mốc hủy tự do buổi học thử: phải hủy trước giờ học đầu tiên ít nhất chừng này.</summary>
    private const int TrialCancelWindowHours = 2;

    /// <summary>
    /// True nếu còn chưa đủ <see cref="TrialCancelWindowHours"/> giờ tới buổi học sớm nhất chưa bị
    /// hủy. Chỉ có ý nghĩa ở giai đoạn tiền buổi 1 (nếu đã có buổi nào start/settle,
    /// <see cref="HasStartedOrSettledLesson"/> đã chặn từ trước rồi).
    /// </summary>
    private static bool IsWithinTrialCancelWindow(Booking booking, DateTime now)
    {
        var firstSessionStart = booking.ClassSessions
            .Where(s => s.Status != Cancelled)
            .Select(s => (DateTime?)s.Scheduledstart)
            .Min();

        return firstSessionStart.HasValue && firstSessionStart.Value - now < TimeSpan.FromHours(TrialCancelWindowHours);
    }

    public async Task<List<BookedSlotResponse>> GetTutorBookedSlotsAsync(
        string tutorId,
        DateTime startDate,
        DateTime endDate)
    {
        static DateTime NormalizeUtc(DateTime value) => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };

        var fromUtc = NormalizeUtc(startDate);
        var toUtc = NormalizeUtc(endDate);

        var classSessions = await context.ClassSessions
            .Where(l => l.Tutorid == tutorId
                && l.Scheduledend > fromUtc
                && l.Scheduledstart < toUtc
                && l.Status != Cancelled
                && l.Status != CancelledNoshow
                && l.Status != Completed
                && l.Status != NoShow)
            .Select(l => new { l.Scheduledstart, l.Scheduledend, l.Bookingid, BookingStatus = l.Booking!.Status })
            .ToListAsync();

        // Khung giờ chỉ thực sự "khóa" khi gia sư đã accept 1 booking (deposit_paid+) cho đúng
        // khung giờ đó — pending_tutor/pending_payment chỉ được đếm để cảnh báo, không khóa gì cả
        // (xem BookingScheduleLockPolicy).
        return classSessions
            .GroupBy(l => (l.Scheduledstart, l.Scheduledend))
            .Select(g => new BookedSlotResponse
            {
                ScheduledStart = DateTime.SpecifyKind(g.Key.Scheduledstart, DateTimeKind.Utc),
                ScheduledEnd = DateTime.SpecifyKind(g.Key.Scheduledend, DateTimeKind.Utc),
                IsLocked = g.Any(x => BookingScheduleLockPolicy.IsLockingStatus(x.BookingStatus)),
                PendingCount = g.Where(x => x.BookingStatus == BookingStatus.PendingTutor)
                    .Select(x => x.Bookingid)
                    .Distinct()
                    .Count()
            })
            .Where(r => r.IsLocked || r.PendingCount > 0)
            .OrderBy(r => r.ScheduledStart)
            .ToList();
    }


    private async Task ValidateSlotsAsync(
        string tutorId,
        IReadOnlyList<ClassSessionSlot> classSessionSlots)
    {
        if (classSessionSlots.Count == 0)
            throw new BookingException(BookingErrorCodes.InvalidSchedule, "Không tìm thấy lịch học hợp lệ", 400);

        var tutorAvailabilities = await context.Tutoravailabilities
            .Where(a => a.Tutorid == tutorId)
            .ToListAsync();

        foreach (var slot in classSessionSlots)
        {
            // slot.Start/End are UTC — compare directly against UTC availability rows
            var isoDayUtc = slot.Start.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)slot.Start.DayOfWeek;
            var startUtc = TimeOnly.FromTimeSpan(slot.Start.TimeOfDay);
            var endUtc = TimeOnly.FromTimeSpan(slot.End.TimeOfDay);

            // Local (+7) conversions kept only for human-readable error messages and logging —
            // slot.Start/End ở trên là UTC, phải cộng lệch múi giờ thật thì message mới đúng giờ
            // VN hiển thị cho người dùng (trước đây gán thẳng UTC nên báo nhầm "00:00" thay vì "07:00").
            var startVn = slot.Start.AddHours(7);
            var endVn = slot.End.AddHours(7);
            var bookingDate = DateOnly.FromDateTime(startVn);

            if (slot.Start <= TimeZoneHelper.UtcNow)
                throw new BookingException(BookingErrorCodes.SlotInPast,
                    $"Khung giờ {startVn:dd/MM/yyyy HH:mm}-{endVn:HH:mm} đã ở trong quá khứ, vui lòng chọn giờ khác", 400);

            if (!BookingLeadTimePolicy.IsFarEnoughToBook(TimeZoneHelper.UtcNow, slot.Start))
                throw new BookingException(BookingErrorCodes.SlotInPast,
                    $"Khung giờ {startVn:dd/MM/yyyy HH:mm}-{endVn:HH:mm} quá sát. "
                    + $"Vui lòng đặt trước ít nhất {BookingLeadTimePolicy.MinimumLeadHours} giờ để gia sư kịp sắp xếp.",
                    400);

            // Debug logging
            logger.LogInformation(
                "Validating slot (UTC): DayOfWeek={ISO}, Time={StartTime}-{EndTime} | Local(+7): {Date} {StartVn}-{EndVn}",
                isoDayUtc,
                startUtc,
                endUtc,
                startVn.ToString("dd/MM/yyyy"),
                startVn.ToString("HH:mm"),
                endVn.ToString("HH:mm"));

            var covered = false;
            foreach (var a in tutorAvailabilities)
            {
                if (!a.Starttime.HasValue || !a.Endtime.HasValue || !a.Dayofweek.HasValue) continue;

                logger.LogInformation(
                    "Checking availability (UTC): DayOfWeek={AvailDay}, Time={AvailStart}-{AvailEnd}, Match: Day={DayMatch}, Start={StartMatch}, End={EndMatch}",
                    a.Dayofweek.Value,
                    a.Starttime.Value,
                    a.Endtime.Value,
                    a.Dayofweek.Value == isoDayUtc,
                    a.Starttime.Value <= startUtc,
                    a.Endtime.Value >= endUtc);

                if (a.Dayofweek.Value == isoDayUtc
                    && a.Starttime.Value <= startUtc
                    && a.Endtime.Value >= endUtc)
                {
                    covered = true;
                    break;
                }
            }

            if (!covered)
                throw new BookingException(BookingErrorCodes.ScheduleNotInAvailability,
                    $"Slot {startVn:dd/MM/yyyy HH:mm}-{endVn:HH:mm} nằm ngoài lịch rảnh của gia sư", 400);

            // Chỉ chặn khi khung giờ đã thực sự bị "khóa" (gia sư đã accept 1 booking khác —
            // deposit_paid trở lên). Booking đang pending_tutor/pending_payment không chặn ai cả,
            // kể cả chính người đã tạo nó — lịch luôn mở cho tới khi gia sư chủ động xác nhận.
            var hasConflict = await context.ClassSessions.AnyAsync(l =>
                l.Tutorid == tutorId
                && l.Status != Cancelled
                && l.Status != CancelledNoshow
                && l.Status != Completed
                && l.Status != NoShow
                && l.Booking != null
                && (l.Booking.Status == BookingStatus.Accepted
                    || l.Booking.Status == BookingStatus.DepositPaid
                    || l.Booking.Status == BookingStatus.PendingRemainingPayment
                    || l.Booking.Status == BookingStatus.Paid
                    || l.Booking.Status == BookingStatus.Ongoing
                    || l.Booking.Status == BookingStatus.Completed)
                && l.Scheduledstart < slot.End
                && l.Scheduledend > slot.Start);

            if (hasConflict)
                throw new BookingException(BookingErrorCodes.ScheduleConflict,
                    $"Gia sư đã có lịch dạy vào {startVn:HH:mm} ngày {startVn:dd/MM/yyyy}. Vui lòng chọn khung giờ khác.", 409);
        }
    }

    private static int ResolveTotalSessions(CreateBookingRequest dto, Tutorpackage package)
    {
        if (package.Packagetype == Tutorpackage.FlexiblePackageType)
        {
            var count = dto.FlexibleSlots?.Count ?? 0;
            if (count <= 3)
                throw new BookingException(BookingErrorCodes.InvalidSchedule, "Package flexible yêu cầu chọn ít nhất 4 buổi học", 400);
            if (dto.TotalSessions.HasValue && dto.TotalSessions.Value != count)
                throw new BookingException(BookingErrorCodes.InvalidSchedule, "TotalSessions phải bằng số lượng flexibleSlots", 400);
            return count;
        }

        if (!dto.TotalSessions.HasValue || dto.TotalSessions.Value <= 3)
            throw new BookingException(BookingErrorCodes.InvalidSchedule, "Package fixed yêu cầu TotalSessions ít nhất 4 buổi", 400);

        return dto.TotalSessions.Value;
    }

    private static List<ClassSessionSlot> GenerateFixedPackageSlots(Tutorpackage package, DateTime startDate, int totalSessions)
    {
        if (package.Tutorpackagefixedslots.Count == 0)
            throw new BookingException(BookingErrorCodes.InvalidSchedule, "Package cố định chưa có khung giờ", 400);

        // FE sends UTC — use directly
        var startDay = (startDate.Kind == DateTimeKind.Utc ? startDate : DateTime.SpecifyKind(startDate, DateTimeKind.Utc)).Date;
        var todayUtc = TimeZoneHelper.UtcNow.Date;
        var currentDate = startDay >= todayUtc ? startDay : todayUtc;

        // Fixed slots stored in UTC — use directly, no conversion needed
        var fixedSlots = package.Tutorpackagefixedslots
            .Select(s => (utcDay: s.Dayofweek, utcStart: s.Starttime, utcEnd: s.Endtime))
            .OrderBy(s => s.utcDay)
            .ThenBy(s => s.utcStart)
            .ToList();
        var result = new List<ClassSessionSlot>();

        while (result.Count < totalSessions)
        {
            var isoDayOfWeek = currentDate.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)currentDate.DayOfWeek;

            foreach (var (slotDay, slotStart, slotEnd) in fixedSlots.Where(s => s.utcDay == isoDayOfWeek))
            {
                if (result.Count >= totalSessions) break;

                var start = new DateTime(currentDate.Year, currentDate.Month, currentDate.Day,
                    slotStart.Hour, slotStart.Minute, 0, DateTimeKind.Utc);
                var end = new DateTime(currentDate.Year, currentDate.Month, currentDate.Day,
                    slotEnd.Hour, slotEnd.Minute, 0, DateTimeKind.Utc);
                result.Add(new ClassSessionSlot(start, end));
            }

            currentDate = currentDate.AddDays(1);
        }

        return result;
    }

    private static List<ClassSessionSlot> GenerateFlexibleSlots(CreateBookingRequest dto, int durationMinutes, int totalSessions)
    {
        var slots = dto.FlexibleSlots ?? [];
        if (slots.Count != totalSessions)
            throw new BookingException(BookingErrorCodes.InvalidSchedule,
                $"Package linh hoạt yêu cầu chọn đúng {totalSessions} buổi học", 400);

        var duration = TimeSpan.FromMinutes(durationMinutes);
        // FE sends UTC with Z — use directly
        return slots
            .Select(s =>
            {
                var start = s.ScheduledStart.Kind == DateTimeKind.Utc
                    ? s.ScheduledStart
                    : DateTime.SpecifyKind(s.ScheduledStart, DateTimeKind.Utc);
                var end = s.ScheduledEnd.Kind == DateTimeKind.Utc
                    ? s.ScheduledEnd
                    : DateTime.SpecifyKind(s.ScheduledEnd, DateTimeKind.Utc);
                    
                if (end <= start || Math.Abs((end - start - duration).TotalMinutes) > 1)
                    throw new BookingException(BookingErrorCodes.InvalidSchedule,
                        $"Mỗi buổi học phải kéo dài {durationMinutes} phút", 400);
                return new ClassSessionSlot(start, end);
            })
            .OrderBy(s => s.Start)
            .ToList();
    }

    private static BookingResponse MapToResponse(Booking b,
        Studentprofile? student, Tutorprofile? tutor, Subject? subject,
        decimal parentFeePercent, decimal tutorFeePercent)
    {
        var grade = b.Tutorsubjectgradeprice?.Gradelevel ?? student?.GradelevelNavigation;
        var classSessions = b.ClassSessions?
            .OrderBy(l => l.Scheduledstart)
            .Select((l, i) => new BookingClassSessionSlotResponse
            {
                ClassSessionId = l.Classsessionid,
                SessionIndex = i + 1,
                ScheduledStart = l.Scheduledstart,
                ScheduledEnd = l.Scheduledend,
                Status = l.Status,
                ClassSessionPrice = l.Lessonprice,
                IsContinuation = l.Iscontinuation,
                IsDisputeRelearn = l.Isdisputerelearn,
                OriginalClassSessionId = l.Originalsessionid
            })
            .ToList();
        var baseAmount = Math.Max((b.Totalamount ?? 0m) - (b.Discountapplied ?? 0m), 0m);
        var calculatedFees = BookingFeeCalculator.Calculate(baseAmount, parentFeePercent, tutorFeePercent);
        var parentFee = b.Parentfee ?? calculatedFees.ParentFee;
        var tutorServiceFee = b.Platformfee.HasValue
            ? Math.Max(b.Platformfee.Value - parentFee, 0m)
            : calculatedFees.TutorFeeCut;
        var tutorReceivable = Math.Max(baseAmount - tutorServiceFee, 0m);

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
                SubjectName = subject.Subjectname,
                IsActive = subject.IsActive,
                Slug = subject.Slug,
                IconUrl = subject.IconUrl
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
                LevelOrder = grade.Levelorder,
                IsActive = grade.IsActive
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
            BaseAmount = baseAmount,
            ParentFee = parentFee,
            TutorServiceFee = tutorServiceFee,
            TutorReceivable = tutorReceivable,
            FinalPrice = b.Finalprice,
            PlatformFee = b.Platformfee,
            Status = b.Status,
            CreatedByRole = b.Createdbyrole,
            PaymentStatus = b.Paymentstatus,
            PaymentCode = b.Paymentrequests
                .OrderByDescending(r => r.Createdat)
                .ThenByDescending(r => r.Paymentrequestid)
                .Select(r => r.Paymentlinkid)
                .FirstOrDefault(),
            Schedule = classSessions?.Select(l => 
            {
                // Convert C# DayOfWeek (0=Sunday, 1=Monday...) to ISO format (1=Monday, 7=Sunday)
                var isoDayOfWeek = l.ScheduledStart.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)l.ScheduledStart.DayOfWeek;
                return new ScheduleItemResponse
                {
                    DayOfWeek = isoDayOfWeek,
                    StartTime = l.ScheduledStart.ToString("HH:mm"),
                    EndTime = l.ScheduledEnd.ToString("HH:mm")
                };
            }).ToList(),
            ClassSessions = classSessions,
            StartDate = b.Startdate,
            // Pattern này đúng cả local lẫn cloud deployment.
            CreatedAt = b.Createdat,
            PaymentDueAt = b.Paymentdueat,
            ResponseDeadline = b.Responsedeadline.HasValue
                ? DateTime.SpecifyKind(b.Responsedeadline.Value, DateTimeKind.Utc)
                : null,
            DepositAmount = b.Depositamount
                ?? (b.Finalprice.HasValue && b.Totalsessions.HasValue && b.Totalsessions > 0
                    ? Math.Floor(b.Finalprice.Value / b.Totalsessions.Value)
                    : (decimal?)null),
            RemainingAmount = b.Remainingamount,
            DepositPaidAt = b.Depositpaidat,
            RemainingPaidAt = b.Remainingpaidat,
            EscrowStatus = b.Escrowstatus,
            RefundAmount = b.Refundamount,
            RefundStatus = b.Refundstatus,
            CancellationReason = b.Cancellationreason,
            CancelledBy = b.Cancelledby,
            CancelledAt = b.Cancelledat
        };
    }

    private sealed record ClassSessionSlot(DateTime Start, DateTime End);

}
