using Microsoft.EntityFrameworkCore;
using MV.ApplicationLayer.Helpers;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.Entities;
using MV.DomainLayer.Exceptions;
using MV.DomainLayer.Helpers;

namespace MV.ApplicationLayer.Services;

public partial class BookingService
{
    // ─── Promotion Methods ────────────────────────────────────────────────

    public async Task<PromotionValidateResponse> ValidatePromotionAsync(string code, decimal orderValue)
    {
        if (string.IsNullOrWhiteSpace(code))
            return new PromotionValidateResponse { Valid = false, Message = "Mã khuyến mãi không được để trống." };

        var codeTrimmed = code.Trim().ToLower();
        var promo = await context.Promotions.FirstOrDefaultAsync(p =>
            p.Code != null && p.Code.ToLower() == codeTrimmed && p.Isactive == true);
        if (promo == null)
            return new PromotionValidateResponse { Valid = false, Message = "Mã khuyến mãi không hợp lệ hoặc đã hết hạn." };

        var now = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;
        if (promo.Startdate.HasValue && promo.Startdate.Value > now)
            return new PromotionValidateResponse { Valid = false, Message = "Mã khuyến mãi chưa đến thời gian áp dụng." };
        if (promo.Enddate.HasValue && promo.Enddate.Value < now)
            return new PromotionValidateResponse { Valid = false, Message = "Mã khuyến mãi đã hết hạn." };
        if (promo.Usagelimit.HasValue && promo.Usagecount >= promo.Usagelimit)
            return new PromotionValidateResponse { Valid = false, Message = "Mã khuyến mãi đã đạt giới hạn sử dụng." };
        if (promo.Minordervalue.HasValue && orderValue < promo.Minordervalue.Value)
            return new PromotionValidateResponse
            {
                Valid = false,
                Message = "Đơn hàng chưa đạt giá trị tối thiểu để áp dụng mã khuyến mãi.",
                MinOrderValue = promo.Minordervalue
            };

        return new PromotionValidateResponse
        {
            Valid = true,
            Code = promo.Code,
            DiscountType = promo.Discounttype,
            DiscountValue = promo.Discountvalue,
            MaxDiscountAmount = promo.Maxdiscountamount,
            MinOrderValue = promo.Minordervalue
        };
    }

    public async Task<BookingResponse?> ApplyPromotionAsync(int bookingId, string userId, string promotionCode)
    {
        var booking = await context.Bookings
            .Include(b => b.Student)
            .ThenInclude(s => s!.GradelevelNavigation)
            .Include(b => b.Tutor).ThenInclude(t => t!.Tutor)
            .Include(b => b.Tutorsubjectgradeprice).ThenInclude(p => p!.Subject)
            .Include(b => b.Tutorsubjectgradeprice).ThenInclude(p => p!.Gradelevel)
            .Include(b => b.Package)
            .Include(b => b.ClassSessions)
            .FirstOrDefaultAsync(b => b.Bookingid == bookingId &&
                (b.Parentid == userId || b.Studentid == userId || b.Student.Linkeduserid == userId));

        if (booking == null ||
            booking.Status != BookingStatus.PendingPayment ||
            !string.Equals(booking.Paymentstatus, PaymentStatus.Pending, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (!string.IsNullOrEmpty(booking.Paymentcode))
        {
            throw new BookingException(
                BookingErrorCodes.InvalidBookingStatus,
                "Không thể áp dụng khuyến mãi sau khi đã tạo link thanh toán. Vui lòng áp dụng mã trước khi mở thông tin thanh toán.",
                409);
        }

        var price = booking.Totalamount ?? 0;
        var previousPromotionId = booking.Promotionid;
        var promoResult = await ResolvePromotionAsync(promotionCode, price);
        if (promoResult.PromotionId == null)
            return null;

        booking.Promotionid = promoResult.PromotionId;
        booking.Discountapplied = promoResult.DiscountAmount;
        var baseAmount = Math.Max(price - promoResult.DiscountAmount, 0);
        var fees = BookingFeeCalculator.Calculate(baseAmount);
        var totalSessions = ResolvePaymentPhaseSessionCount(booking);
        var (depositAmount, remainingAmount) = BookingFeeCalculator.CalculatePaymentPhases(
            fees.FinalPrice, totalSessions);

        booking.Finalprice = fees.FinalPrice;
        booking.Platformfee = fees.PlatformFee;
        booking.Parentfee = fees.ParentFee;
        booking.Tutorfee = fees.TutorReceivable;
        booking.Depositamount = depositAmount;
        booking.Remainingamount = remainingAmount;
        booking.Updatedat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;
        ClearCachedPayosLink(booking);

        await UpdatePromotionUsageCountAsync(previousPromotionId, promoResult.PromotionId.Value);

        await context.SaveChangesAsync();
        return MapToResponse(booking, booking.Student, booking.Tutor, booking.Tutorsubjectgradeprice?.Subject);
    }

    public async Task<PromotionCreatedResponse> CreatePromotionAsync(CreatePromotionRequest dto)
    {
        var code = dto.Code.Trim();
        if (await context.Promotions.AnyAsync(p => p.Code != null && p.Code.ToLower() == code.ToLower()))
            throw new BookingException(BookingErrorCodes.PromotionCodeExists, "Mã khuyến mãi đã tồn tại", 409);

        var promo = new Promotion
        {
            Code = code,
            Description = dto.Description,
            Discounttype = dto.DiscountType,
            Discountvalue = dto.DiscountValue,
            Maxdiscountamount = dto.MaxDiscountAmount,
            Minordervalue = dto.MinOrderValue,
            Startdate = dto.StartDate,
            Enddate = dto.EndDate,
            Usagelimit = dto.UsageLimit,
            Usagecount = 0,
            Isactive = dto.IsActive,
            Createdat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow
        };
        context.Promotions.Add(promo);
        await context.SaveChangesAsync();
        return new PromotionCreatedResponse { PromotionId = promo.Promotionid, Code = promo.Code };
    }

    public async Task<PromotionListResponse> GetAllPromotionsAsync(
        int page,
        int pageSize,
        bool? isActive,
        string? search,
        CancellationToken ct = default)
    {
        var query = context.Promotions.AsNoTracking();

        if (isActive.HasValue)
            query = query.Where(p => p.Isactive == isActive.Value);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var kw = search.ToLower();
            query = query.Where(p =>
                (p.Code != null && p.Code.ToLower().Contains(kw)) ||
                (p.Description != null && p.Description.ToLower().Contains(kw)));
        }

        var totalCount = await query.CountAsync(ct);

        var raw = await query
            .OrderByDescending(p => p.Createdat)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new
            {
                p.Promotionid,
                p.Code,
                p.Description,
                p.Discounttype,
                p.Discountvalue,
                p.Maxdiscountamount,
                p.Minordervalue,
                p.Startdate,
                p.Enddate,
                p.Usagelimit,
                p.Usagecount,
                p.Isactive,
                p.Createdat
            })
            .ToListAsync(ct);

        var now = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;

        var items = raw.Select(p => new PromotionResponse
        {
            PromotionId       = p.Promotionid,
            Code              = p.Code,
            Description       = p.Description,
            DiscountType      = p.Discounttype,
            DiscountValue     = p.Discountvalue,
            MaxDiscountAmount = p.Maxdiscountamount,
            MinOrderValue     = p.Minordervalue,
            StartDate         = p.Startdate,
            EndDate           = p.Enddate,
            UsageLimit        = p.Usagelimit,
            UsageCount        = p.Usagecount,
            IsActive          = p.Isactive,
            CreatedAt         = p.Createdat,
            RuntimeStatus     = ResolveRuntimeStatus(p.Isactive, p.Startdate, p.Enddate, p.Usagelimit, p.Usagecount, now)
        }).ToList();

        return new PromotionListResponse
        {
            Items      = items,
            TotalCount = totalCount,
            Page       = page,
            PageSize   = pageSize
        };
    }

    public async Task<PromotionResponse?> UpdatePromotionAsync(
        int promotionId,
        UpdatePromotionRequest dto,
        CancellationToken ct = default)
    {
        var promo = await context.Promotions
            .FirstOrDefaultAsync(p => p.Promotionid == promotionId, ct);

        if (promo is null) return null;

        // Validate: StartDate phải trước EndDate (nếu cả hai được cung cấp)
        var effectiveStart = dto.StartDate ?? promo.Startdate;
        var effectiveEnd   = dto.EndDate   ?? promo.Enddate;
        if (effectiveStart.HasValue && effectiveEnd.HasValue && effectiveStart > effectiveEnd)
            throw new BookingException(
                BookingErrorCodes.InvalidInput,
                "StartDate phải trước EndDate.",
                400);

        // Chỉ cập nhật field nào được gửi lên (PATCH semantics)
        if (dto.Description    != null) promo.Description       = dto.Description;
        if (dto.DiscountType   != null) promo.Discounttype       = dto.DiscountType;
        if (dto.DiscountValue  != null) promo.Discountvalue      = dto.DiscountValue;
        if (dto.MaxDiscountAmount != null) promo.Maxdiscountamount = dto.MaxDiscountAmount;
        if (dto.MinOrderValue  != null) promo.Minordervalue      = dto.MinOrderValue;
        if (dto.StartDate      != null) promo.Startdate          = dto.StartDate;
        if (dto.EndDate        != null) promo.Enddate            = dto.EndDate;
        if (dto.UsageLimit     != null) promo.Usagelimit         = dto.UsageLimit;
        if (dto.IsActive       != null) promo.Isactive           = dto.IsActive;

        await context.SaveChangesAsync(ct);

        var now = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;
        return new PromotionResponse
        {
            PromotionId       = promo.Promotionid,
            Code              = promo.Code,
            Description       = promo.Description,
            DiscountType      = promo.Discounttype,
            DiscountValue     = promo.Discountvalue,
            MaxDiscountAmount = promo.Maxdiscountamount,
            MinOrderValue     = promo.Minordervalue,
            StartDate         = promo.Startdate,
            EndDate           = promo.Enddate,
            UsageLimit        = promo.Usagelimit,
            UsageCount        = promo.Usagecount,
            IsActive          = promo.Isactive,
            CreatedAt         = promo.Createdat,
            RuntimeStatus     = ResolveRuntimeStatus(promo.Isactive, promo.Startdate, promo.Enddate, promo.Usagelimit, promo.Usagecount, now)
        };
    }

    public async Task<(bool Found, bool AlreadyInactive)> DeactivatePromotionAsync(
        int promotionId,
        CancellationToken ct = default)
    {
        var promo = await context.Promotions
            .FirstOrDefaultAsync(p => p.Promotionid == promotionId, ct);

        if (promo is null) return (Found: false, AlreadyInactive: false);

        // Idempotent: đã inactive rồi thì báo để controller trả message phù hợp
        if (promo.Isactive == false) return (Found: true, AlreadyInactive: true);

        promo.Isactive = false;
        await context.SaveChangesAsync(ct);

        return (Found: true, AlreadyInactive: false);
    }

    /// <summary>Tính trạng thái thực tế của promotion theo thời gian hiện tại.</summary>
    private static string ResolveRuntimeStatus(
        bool? isActive,
        DateTime? startDate,
        DateTime? endDate,
        int? usageLimit,
        int? usageCount,
        DateTime now)
    {
        if (isActive != true) return "Inactive";
        if (startDate.HasValue && startDate.Value > now) return "Upcoming";
        if (endDate.HasValue && endDate.Value < now) return "Expired";
        if (usageLimit.HasValue && (usageCount ?? 0) >= usageLimit.Value) return "Exhausted";
        return "Active";
    }

    // ─── Private helpers ─────────────────────────────────────────────────

    private async Task<(int? PromotionId, decimal DiscountAmount)> ResolvePromotionAsync(string code, decimal orderValue)
    {
        var validate = await ValidatePromotionAsync(code, orderValue);
        if (!validate.Valid)
        {
            if (validate.Message?.Contains("minimum") == true)
                throw new BookingException(BookingErrorCodes.PromotionMinOrder, validate.Message, 409);
            throw new BookingException(BookingErrorCodes.PromotionInvalid, validate.Message ?? "Mã khuyến mãi không hợp lệ", 409);
        }

        var codeNorm = code.Trim().ToLower();
        var promo = await context.Promotions.FirstAsync(p => p.Code != null && p.Code.ToLower() == codeNorm);
        var discount = 0m;
        if ((promo.Discounttype ?? "").ToLowerInvariant() == DiscountType.Percent && promo.Discountvalue.HasValue)
            discount = orderValue * (promo.Discountvalue.Value / 100m);
        else if ((promo.Discounttype ?? "").ToLowerInvariant() == DiscountType.Fixed && promo.Discountvalue.HasValue)
            discount = promo.Discountvalue.Value;
        if (promo.Maxdiscountamount.HasValue && discount > promo.Maxdiscountamount.Value)
            discount = promo.Maxdiscountamount.Value;
        return (promo.Promotionid, Math.Round(discount, 2));
    }

    private static int ResolvePaymentPhaseSessionCount(Booking booking)
    {
        if (booking.Totalsessions.GetValueOrDefault() > 0)
            return booking.Totalsessions!.Value;

        return booking.ClassSessions.Count > 0 ? booking.ClassSessions.Count : 1;
    }

    private static void ClearCachedPayosLink(Booking booking)
    {
        booking.Paymentcode = null;
        booking.Payosbin = null;
        booking.Payosaccountnumber = null;
        booking.Payosaccountname = null;
        booking.Payosdescription = null;
        booking.Payoscheckouturl = null;
        booking.Payosqrcode = null;
    }

    private async Task UpdatePromotionUsageCountAsync(int? previousPromotionId, int newPromotionId)
    {
        if (previousPromotionId == newPromotionId)
            return;

        if (previousPromotionId.HasValue)
        {
            var previousPromo = await context.Promotions
                .FirstOrDefaultAsync(p => p.Promotionid == previousPromotionId.Value);
            if (previousPromo != null)
                previousPromo.Usagecount = Math.Max((previousPromo.Usagecount ?? 0) - 1, 0);
        }

        var newPromo = await context.Promotions.FirstAsync(p => p.Promotionid == newPromotionId);
        newPromo.Usagecount = (newPromo.Usagecount ?? 0) + 1;
    }
}
