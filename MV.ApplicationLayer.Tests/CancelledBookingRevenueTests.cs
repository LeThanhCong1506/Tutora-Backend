using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using MV.ApplicationLayer.Services;
using MV.DomainLayer.Constants;
using MV.DomainLayer.Entities;
using MV.InfrastructureLayer.DBContext;
using Xunit;

namespace MV.ApplicationLayer.Tests;

/// <summary>
/// Tiền của một khoá bị HUỶ GIỮA CHỪNG phải đi vào báo cáo doanh thu, không được biến mất.
///
/// Trước đây <c>AdminRevenueAnalyticsService</c> lọc mọi thống kê theo
/// <c>RevenueBookingStatuses</c> (paid / deposit_paid / pending_remaining_payment / ongoing /
/// completed), nên một khoá bị admin huỷ rơi khỏi TOÀN BỘ báo cáo: GMV 0, hoa hồng 0, gia sư
/// 0 — trong khi ví đã thật sự chuyển tiền cho cả ba bên. Trang báo cáo khi đó hiện
/// "Tiền phụ huynh trả 0đ" ngay cạnh "Đã hoàn tiền 90.000đ".
///
/// Kịch bản dưới đây là ví dụ nghiệp vụ đã chốt, dùng làm mốc cho mọi thay đổi sau này:
/// khoá 100.000đ/10 buổi, phí sàn 5% + 5%, phụ huynh trả 105.000đ, học 1 buổi rồi admin huỷ.
///
///     gia sư           9.500  = (Tutorfee 95.000 / 10 buổi) × 1 buổi đã dạy
///     hoàn phụ huynh  90.000  = (giá gốc 100.000 / 10) × 9 buổi chưa học, KHÔNG gồm phí
///     Tutora giữ       5.500  = 105.000 − 9.500 − 90.000
///                             = hoa hồng 1.000 của buổi đã dạy
///                             + 4.500 phí dịch vụ không hoàn của 9 buổi bị huỷ
///
/// Con số 5.500 KHÔNG suy được bằng công thức "đơn giá hoa hồng × số buổi đã dạy" (ra 1.000),
/// nên báo cáo phải lấy từ sổ ví — đó là lý do <c>BuildClosedBookings</c> tồn tại.
/// </summary>
public class CancelledBookingRevenueTests
{
    private const string TutorId = "tutor-1";
    private const string ParentId = "parent-1";
    private const string StudentId = "student-1";
    private const int BookingId = 1;

    private static readonly DateTime CreatedAt = new(2026, 8, 1, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime DeliveredAt = new(2026, 8, 5, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime CancelledAt = new(2026, 8, 10, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime From = new(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime To = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task AdminCancelsMidCourse_PlatformKeepsWhatTheLedgerSays()
    {
        await using var context = CreateContext();
        SeedCancelledCourse(context);
        await context.SaveChangesAsync();

        var overview = await CreateService(context).GetOverviewAsync(From, To);
        var s = overview.Summary;

        // Khoá đã huỷ vẫn là một giao dịch có thật: phụ huynh đã trả đủ 105.000đ.
        Assert.Equal(105_000m, s.Gmv);
        Assert.Equal(100_000m, s.BaseAmount);

        // Đúng 5.500đ — con số nghiệp vụ đã chốt, không phải 1.000đ của công thức đơn giá.
        Assert.Equal(5_500m, s.CommissionEarned);
        Assert.Equal(5_500m, s.RecognisedRevenue);
        Assert.Equal(5_500m, s.CommissionFromCancelled);

        // 4.500đ hoa hồng còn lại mất hẳn, KHÔNG được đếm là "chờ ghi nhận".
        Assert.Equal(10_000m, s.CommissionSold);
        Assert.Equal(4_500m, s.CommissionLost);
        Assert.Equal(0m, s.CommissionSold - s.CommissionEarned - s.CommissionLost);

        // Khoá đã đóng: 9 buổi chưa dạy đã bị huỷ và hoàn tiền, không còn là nghĩa vụ dịch vụ.
        Assert.Equal(0m, s.DeferredRevenue);
    }

    [Fact]
    public async Task AdminCancelsMidCourse_TutorKeepsTheSessionTheyTaught()
    {
        await using var context = CreateContext();
        SeedCancelledCourse(context);
        await context.SaveChangesAsync();

        var tutors = await CreateService(context).GetTutorRevenueAsync(From, To, top: 10);
        var row = Assert.Single(tutors.Tutors);

        Assert.Equal(9_500m, row.TutorEarnings);
        Assert.Equal(1, row.SessionsDelivered);

        // Tab Gia sư chỉ báo vế PHÍ GIA SƯ: 5% của đúng 1 buổi đã dạy = 500đ. KHÔNG phải
        // 5.500đ — 5.000đ còn lại là phí dịch vụ PHỤ HUYNH trả, thuộc tab Khách hàng.
        Assert.Equal(500m, row.TutorFeeRevenue);
        Assert.Equal(500m, tutors.TotalTutorFeeRevenue);
    }

    /// <summary>
    /// Vế đối xứng của test trên: 5.000đ phí dịch vụ phải hiện ở tab KHÁCH HÀNG, và hai tab
    /// cộng lại đúng bằng 5.500đ mà sổ ví nói Tutora giữ được.
    ///
    /// Đây là bất biến quan trọng nhất của việc tách nguồn (01/09/2026): chia đôi để đọc cho
    /// đúng chỗ, KHÔNG được làm mất hay nhân đôi đồng nào.
    /// </summary>
    [Fact]
    public async Task AdminCancelsMidCourse_ServiceFeeShowsOnTheCustomerTab()
    {
        await using var context = CreateContext();
        SeedCancelledCourse(context);
        await context.SaveChangesAsync();

        var service = CreateService(context);
        var customers = await service.GetCustomerRevenueAsync(From, To, top: 10);
        var tutors = await service.GetTutorRevenueAsync(From, To, top: 10);

        // Khoá đã chốt sổ: không còn gì để chờ, phí dịch vụ đã hết đường hoàn.
        Assert.Equal(5_000m, customers.Summary.ServiceFeeRecognised);
        Assert.Equal(0m, customers.Summary.ServiceFeePending);

        var parent = Assert.Single(customers.Parents);
        Assert.Equal(5_000m, parent.ServiceFeeRecognised);
        Assert.Equal(0m, parent.ServiceFeePending);

        // Hai nguồn cộng lại đúng bằng số Tutora thực giữ theo sổ ví.
        Assert.Equal(
            5_500m,
            tutors.TotalTutorFeeRevenue + customers.Summary.ServiceFeeRecognised);
    }

    /// <summary>
    /// Khoá đang chạy, mới trả cọc và CHƯA qua buổi đầu: phí dịch vụ phải nằm trọn ở cột
    /// "đợi ghi nhận" — vì lúc này khách huỷ vẫn được hoàn 100% kể cả phí.
    /// </summary>
    [Fact]
    public async Task BeforeTheFirstSession_ServiceFeeIsAllPending()
    {
        await using var context = CreateContext();
        SeedCancelledCourse(
            context, deliveredSessions: 0, refunded: 0m, releasedToTutor: 0m, paidRemaining: false);
        var booking = context.Bookings.Local.Single();
        booking.Status = BookingStatus.PendingTutor;
        booking.Escrowstatus = EscrowStatus.Holding;
        booking.Cancelledat = null;
        await context.SaveChangesAsync();

        var customers = await CreateService(context).GetCustomerRevenueAsync(From, To, top: 10);

        Assert.Equal(0m, customers.Summary.ServiceFeeRecognised);
        Assert.Equal(5_000m, customers.Summary.ServiceFeePending);
    }

    /// <summary>
    /// Hoàn 100% thì giao dịch đã bị ĐẢO SẠCH — phụ huynh lấy lại từng đồng, nên khoá này phải
    /// nằm ngoài cohort y như khoá chưa ai trả tiền.
    ///
    /// Không chỉ doanh thu bằng 0: cả GMV lẫn "doanh thu tạm tính" lẫn "không thu được" đều phải
    /// bằng 0. Trước 01/09/2026 nó vào cohort theo GIÁ HỢP ĐỒNG, nên một khoá đã đảo sạch vẫn
    /// cộng 105.000đ vào "Tiền phụ huynh trả" và 10.000đ vào tạm tính, rồi lập tức bị trừ lại ở
    /// lát "Không thu được" — ba con số đầu trang cùng phồng lên vì một giao dịch không tồn tại.
    ///
    /// Câu chuyện hoàn tiền không bị giấu: thẻ "Đã hoàn tiền" đếm thẳng từ sổ ví, không lọc cohort.
    /// </summary>
    [Fact]
    public async Task FullyRefundedBooking_StaysOutOfEveryHeadlineNumber()
    {
        await using var context = CreateContext();
        SeedCancelledCourse(context, deliveredSessions: 0, refunded: 105_000m, releasedToTutor: 0m);
        await context.SaveChangesAsync();

        var service = CreateService(context);
        var s = (await service.GetOverviewAsync(From, To)).Summary;

        Assert.Equal(0m, s.CommissionEarned);
        Assert.Equal(0m, s.RecognisedRevenue);
        // Không phải "mất 10.000đ" — chưa từng là giao dịch thì không có gì để mất.
        Assert.Equal(0m, s.CommissionLost);
        Assert.Equal(0m, s.CommissionSold);
        Assert.Equal(0m, s.Gmv);

        // Nhưng khoản hoàn vẫn phải hiện: 90.000đ theo seed mặc định của ví.
        var refunds = (await service.GetRecognitionAsync(From, To)).Refunds;
        Assert.True(refunds.Amount > 0);
    }

    /// <summary>
    /// Vế đối chứng của test trên: hoàn MỘT PHẦN thì khoá ở lại cohort. Phụ huynh vẫn mất tiền
    /// thật (trả 105.000, nhận lại 90.000) và Tutora vẫn giữ 5.500 — giao dịch có thật, và
    /// khoảng chênh 4.500 là khoản mất có thật. Nếu ai đó nới điều kiện loại trừ thành "có dòng
    /// hoàn tiền là loại", test này bắt được ngay.
    /// </summary>
    [Fact]
    public async Task PartiallyRefundedBooking_StaysInTheCohort()
    {
        await using var context = CreateContext();
        SeedCancelledCourse(context);
        await context.SaveChangesAsync();

        var s = (await CreateService(context).GetOverviewAsync(From, To)).Summary;

        Assert.Equal(105_000m, s.Gmv);
        Assert.Equal(10_000m, s.CommissionSold);
        Assert.Equal(5_500m, s.CommissionEarned);
        Assert.Equal(4_500m, s.CommissionLost);
    }

    /// <summary>
    /// Booking huỷ khi CHƯA ai trả đồng nào (gia sư không nhận, quá hạn thanh toán) không phải
    /// giao dịch: nó phải nằm ngoài GMV và ngoài cả "mất do huỷ", nếu không mọi lượt đặt hụt
    /// đều thổi phồng cả hai con số.
    /// </summary>
    [Fact]
    public async Task CancelledWithoutAnyPayment_StaysOutOfTheReportEntirely()
    {
        await using var context = CreateContext();
        SeedCancelledCourse(context, deliveredSessions: 0, refunded: 0m, releasedToTutor: 0m, paid: false);
        await context.SaveChangesAsync();

        var s = (await CreateService(context).GetOverviewAsync(From, To)).Summary;

        Assert.Equal(0m, s.Gmv);
        Assert.Equal(0m, s.CommissionSold);
        Assert.Equal(0m, s.CommissionLost);
        Assert.Equal(0m, s.RecognisedRevenue);
    }

    /// <summary>
    /// Khoá đang chạy: phí sàn chín theo HAI mốc khác nhau vì tiền nằm trong escrow.
    ///
    /// Buổi đầu ĐÃ dạy xong, nên 5.000đ phí dịch vụ hết đường hoàn và thuộc hẳn về Tutora —
    /// từ đây huỷ giữa chừng chỉ hoàn giá gốc của buổi chưa dạy. Còn phí gia sư thì mới chín
    /// đúng 1 buổi đã dạy (500đ): 9 buổi còn lại chưa dạy thì escrow chưa giải ngân, chưa có
    /// gì để cắt.
    ///
    /// Công thức cũ `PlatformFee / TotalSessions × buổi đã dạy` ra 1.000đ — nó treo cả phí phụ
    /// huynh đã nằm trong két vào ngày gia sư dạy xong.
    /// </summary>
    [Fact]
    public async Task OngoingCourse_SplitsServiceFeeFromTutorFee()
    {
        await using var context = CreateContext();
        SeedCancelledCourse(context);
        var booking = context.Bookings.Local.Single();
        booking.Status = BookingStatus.Ongoing;
        booking.Escrowstatus = EscrowStatus.Holding;
        booking.Cancelledat = null;
        // Ví của khoá đang chạy chưa giải ngân: escrow chỉ mở khi cả khoá hoàn tất.
        context.Wallettransactions.RemoveRange(context.Wallettransactions.Local.ToList());
        await context.SaveChangesAsync();

        var s = (await CreateService(context).GetOverviewAsync(From, To)).Summary;

        // 5.000đ phí phụ huynh (đã trả đủ) + 500đ phí gia sư của 1 buổi đã dạy.
        Assert.Equal(5_500m, s.CommissionEarned);
        Assert.Equal(5_500m, s.RecognisedRevenue);
        Assert.Equal(0m, s.CommissionLost);
        // Còn treo: phí gia sư của 9 buổi chưa dạy.
        Assert.Equal(4_500m, s.DeferredRevenue);
        // Hai lát luôn cộng đúng bằng doanh thu tạm tính — điều kiện để vành khuyên đọc được.
        Assert.Equal(s.CommissionSold, s.CommissionEarned + s.CommissionLost + s.DeferredRevenue);
    }

    /// <summary>
    /// Khoá vừa trả cọc và đang chờ gia sư bấm nhận (<c>pending_tutor</c>) — trạng thái mà MỌI
    /// booking đều đi qua, vì <c>PaymentService</c> đặt nó cùng lúc với <c>Depositpaidat</c>.
    ///
    /// Đây là ca hồi quy của booking #318: <c>pending_tutor</c> từng nằm ngoài
    /// <c>RevenueBookingStatuses</c> nên bị coi là ĐÃ CHỐT SỔ, sổ ví bị đọc khi chưa có dòng
    /// hoàn/giải ngân nào, và <c>PlatformKept</c> đội lên tới trần <c>PlatformFee</c> — báo đã
    /// thu trọn phí sàn CẢ KHOÁ trong khi phụ huynh mới trả đúng một buổi và chưa buổi nào được
    /// dạy.
    ///
    /// Đúng ra chưa có đồng nào là tiền thật: buổi đầu chưa diễn ra thì phụ huynh huỷ được và
    /// nhận lại 100% KỂ CẢ phí dịch vụ (<c>BookingService.CancelBooking</c> hoàn trọn
    /// <c>Depositamount</c>). Toàn bộ 10.000đ vẫn là doanh thu tạm tính.
    /// </summary>
    [Fact]
    public async Task PendingTutorAcceptance_RecognisesNothingBeforeTheFirstSession()
    {
        await using var context = CreateContext();
        SeedCancelledCourse(
            context, deliveredSessions: 0, refunded: 0m, releasedToTutor: 0m, paidRemaining: false);
        var booking = context.Bookings.Local.Single();
        booking.Status = BookingStatus.PendingTutor;
        booking.Escrowstatus = EscrowStatus.Holding;
        booking.Cancelledat = null;
        await context.SaveChangesAsync();

        var service = CreateService(context);
        var s = (await service.GetOverviewAsync(From, To)).Summary;

        // Chưa buổi nào dạy → chưa đồng nào là tiền thật. KHÔNG phải 10.000đ, cũng không 500đ.
        Assert.Equal(0m, s.CommissionEarned);
        Assert.Equal(0m, s.RecognisedRevenue);
        // Khoá chưa chốt sổ thì chưa mất gì cả; toàn bộ phí sàn vẫn đang chờ.
        Assert.Equal(0m, s.CommissionLost);
        Assert.Equal(10_000m, s.DeferredRevenue);
        Assert.Equal(10_000m, s.CommissionSold);
        Assert.Equal(105_000m, s.Gmv);

        var row = Assert.Single((await service.GetRecognitionAsync(From, To)).BookingProgress);
        Assert.False(row.Closed);
        Assert.Equal(10_500m, row.CashCollected);
        Assert.Equal(10_000m, row.ContractedFee);
        Assert.Equal(0m, row.RecognisedFee);
    }

    /// <summary>
    /// Khoá hoàn tất bình thường: đọc sổ ví và tính bằng công thức phải ra CÙNG một số. Đây là
    /// điều kiện để mở rộng "đọc sổ ví" sang mọi khoá đã chốt escrow mà không làm xáo trộn nhóm
    /// chiếm đa số dữ liệu.
    /// </summary>
    [Fact]
    public async Task CompletedCourse_LedgerAndFormulaAgree()
    {
        await using var context = CreateContext();
        SeedCancelledCourse(context, deliveredSessions: 10, refunded: 0m, releasedToTutor: 95_000m);
        var booking = context.Bookings.Local.Single();
        booking.Status = BookingStatus.Completed;
        booking.Escrowstatus = EscrowStatus.Released;
        booking.Cancelledat = null;
        await context.SaveChangesAsync();

        var s = (await CreateService(context).GetOverviewAsync(From, To)).Summary;

        Assert.Equal(10_000m, s.CommissionEarned);
        Assert.Equal(10_000m, s.RecognisedRevenue);
        Assert.Equal(0m, s.CommissionLost);
        Assert.Equal(0m, s.DeferredRevenue);
    }

    /// <summary>
    /// Gia sư bị đình chỉ sau khi đã dạy: <c>SuspensionRefundService</c> đóng khoá bằng
    /// <c>Status = completed</c> chứ KHÔNG phải `cancelled`, nên nhóm này từng lọt lưới và bị
    /// tính bằng công thức — báo 1.000đ thay vì 5.500đ. Nay bắt bằng `Escrowstatus`.
    /// </summary>
    [Fact]
    public async Task SuspensionClosedCourse_CountsTheUnrefundedServiceFee()
    {
        await using var context = CreateContext();
        SeedCancelledCourse(context);
        var booking = context.Bookings.Local.Single();
        booking.Status = BookingStatus.Completed;   // đúng như SuspensionRefundService đặt
        booking.Escrowstatus = EscrowStatus.Released;
        booking.Cancelledat = null;
        await context.SaveChangesAsync();

        var s = (await CreateService(context).GetOverviewAsync(From, To)).Summary;

        Assert.Equal(5_500m, s.CommissionEarned);
        Assert.Equal(5_500m, s.RecognisedRevenue);
        Assert.Equal(4_500m, s.CommissionLost);
        Assert.Equal(0m, s.DeferredRevenue);
    }

    /// <summary>
    /// Khách trả đợt 1, học đúng buổi đó rồi bỏ; hệ thống đóng khoá qua
    /// <c>FinalizeBookingEarlyByUserAsync</c> — không hoàn tiền, Tutora giữ nguyên hoa hồng của
    /// buổi đã dạy. Phần hoa hồng 9 buổi còn lại là "không thu được", KHÔNG phải "chờ ghi nhận":
    /// trước đây nó nằm mãi trong nợ dịch vụ dù không bao giờ thành tiền được nữa.
    /// </summary>
    [Fact]
    public async Task AbandonedAfterDeposit_LeavesNothingPending()
    {
        await using var context = CreateContext();
        SeedCancelledCourse(context, refunded: 0m, releasedToTutor: 9_500m, paidRemaining: false);
        var booking = context.Bookings.Local.Single();
        booking.Status = BookingStatus.Completed;
        booking.Escrowstatus = EscrowStatus.Released;
        booking.Cancelledat = null;
        await context.SaveChangesAsync();

        var s = (await CreateService(context).GetOverviewAsync(From, To)).Summary;

        // Chỉ thu được đợt 1 (10.500đ): trừ 9.500đ trả gia sư còn đúng 1.000đ hoa hồng.
        Assert.Equal(1_000m, s.CommissionEarned);
        Assert.Equal(1_000m, s.RecognisedRevenue);
        Assert.Equal(9_000m, s.CommissionLost);
        Assert.Equal(0m, s.DeferredRevenue);
    }

    /// <summary>
    /// Sổ ví thiếu dòng (dữ liệu dev sửa tay) không được làm doanh thu vọt lên: chặn trên ở
    /// `PlatformFee` giữ cho sai sót luôn theo hướng THIẾU, đúng bằng trần mà công thức cũ có
    /// thể cho ra. Đây là điều kiện an toàn của việc đọc sổ ví.
    /// </summary>
    [Fact]
    public async Task MissingLedgerRows_CannotInflateRevenueBeyondTheContractedFee()
    {
        await using var context = CreateContext();
        // Escrow đã đánh dấu released nhưng KHÔNG có dòng ví nào: 105.000đ trôi nổi.
        SeedCancelledCourse(context, deliveredSessions: 10, refunded: 0m, releasedToTutor: 0m);
        var booking = context.Bookings.Local.Single();
        booking.Status = BookingStatus.Completed;
        booking.Escrowstatus = EscrowStatus.Released;
        booking.Cancelledat = null;
        context.Wallettransactions.RemoveRange(context.Wallettransactions.Local.ToList());
        await context.SaveChangesAsync();

        var s = (await CreateService(context).GetOverviewAsync(From, To)).Summary;

        // Trần là hoa hồng đã ký, không phải 105.000đ.
        Assert.Equal(10_000m, s.CommissionEarned);
        Assert.Equal(10_000m, s.RecognisedRevenue);
    }

    /// <summary>
    /// Doanh thu phải ở lại đúng tháng nó thực sự phát sinh. Khoá trả tiền và dạy tháng 8, đóng
    /// sổ tháng 9: cả phí dịch vụ (trả 01/08) lẫn phí gia sư của buổi đã dạy (05/08) đều thuộc
    /// tháng 8, tháng 9 không được cuỗm gì cả.
    ///
    /// Phần chênh khi chốt sổ ra 0 chính vì hai vế đã chín đúng lúc — nếu ai đó quay lại kiểu
    /// dồn cả <c>PlatformKept</c> về ngày đóng sổ thì tháng 8 tụt xuống 500đ và tháng 9 vọt
    /// lên, test này bắt được ngay.
    /// </summary>
    [Fact]
    public async Task ClosingInALaterMonth_LeavesRevenueInTheMonthItWasEarned()
    {
        await using var context = CreateContext();
        SeedCancelledCourse(context);
        var booking = context.Bookings.Local.Single();
        booking.Cancelledat = new DateTime(2026, 9, 10, 9, 0, 0, DateTimeKind.Utc);
        await context.SaveChangesAsync();

        var service = CreateService(context);
        var august = (await service.GetOverviewAsync(From, To)).Summary;
        var september = (await service.GetOverviewAsync(
            new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc))).Summary;

        // 5.000đ phí dịch vụ trả 01/08 + 500đ phí gia sư của buổi dạy 05/08.
        Assert.Equal(5_500m, august.RecognisedRevenue);
        Assert.Equal(0m, september.RecognisedRevenue);
    }

    /// <summary>
    /// Gia sư không phản hồi thì phụ huynh được hoàn CẢ phí dịch vụ
    /// (<c>TutorResponseTimeoutPolicy.ParentRefundAmount</c>) — khác hẳn ca huỷ giữa chừng.
    ///
    /// Vì phí phụ huynh chỉ chín SAU buổi đầu, ca này không ghi nhận gì ngay từ đầu: tháng thu
    /// tiền 0đ, tháng hoàn tiền cũng 0đ. Không có khoản nào phải ghi rồi đảo ngược, nên báo cáo
    /// tháng 8 không bao giờ tự đổi số khi mở lại sau này.
    ///
    /// Bản trước cho phí chín ngay lúc thanh toán nên phải ghi +500 tháng 8 rồi −500 tháng 9 —
    /// đúng tổng nhưng sai cả hai tháng.
    /// </summary>
    [Fact]
    public async Task TutorNeverResponds_NothingIsEverRecognised()
    {
        await using var context = CreateContext();
        SeedCancelledCourse(
            context, deliveredSessions: 0, refunded: 10_500m, releasedToTutor: 0m, paidRemaining: false);
        var booking = context.Bookings.Local.Single();
        booking.Status = BookingStatus.Cancelled;
        booking.Cancelledat = new DateTime(2026, 9, 10, 9, 0, 0, DateTimeKind.Utc);
        await context.SaveChangesAsync();

        var service = CreateService(context);
        var august = (await service.GetOverviewAsync(From, To)).Summary;
        var september = (await service.GetOverviewAsync(
            new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc))).Summary;

        Assert.Equal(0m, august.RecognisedRevenue);     // thu tiền, nhưng phí vẫn hoàn lại được
        Assert.Equal(0m, september.RecognisedRevenue);  // hoàn sạch — không có gì để đảo

        // Hoàn sạch cả cọc → giao dịch bị đảo hết, khoá rơi khỏi cohort: không "thu được",
        // cũng không "mất", và không cộng vào GMV. Xem FullyRefundedBooking_StaysOutOf...
        Assert.Equal(0m, august.CommissionEarned);
        Assert.Equal(0m, august.CommissionLost);
        Assert.Equal(0m, august.Gmv);
    }

    /// <summary>
    /// Dòng của một khoá đã huỷ phải kể trọn câu chuyện tiền ngay trên bảng: khách trả bao
    /// nhiêu, hoàn lại bao nhiêu, Tutora giữ bao nhiêu. Thiếu hai cột tiền mặt thì admin phải
    /// mở sang trang chi tiết từng lịch mới đối chiếu được.
    /// </summary>
    [Fact]
    public async Task BookingTable_ShowsCashAndRefundOnCancelledRow()
    {
        await using var context = CreateContext();
        SeedCancelledCourse(context);
        await context.SaveChangesAsync();

        var data = await CreateService(context).GetRecognitionAsync(From, To);
        var row = Assert.Single(data.BookingProgress);

        Assert.Equal(105_000m, row.CashCollected);
        Assert.Equal(90_000m, row.RefundedAmount);
        Assert.True(row.Closed);
        // "Doanh thu tạm tính" là phí THEO HỢP ĐỒNG, không phải số thực giữ — nếu in `kept` thì
        // cột này trùng khít cột bên cạnh và phần "không thu được" biến mất khỏi chân bảng.
        // Khoảng chênh 10.000 − 5.500 chính là 4.500đ mất hẳn vì huỷ giữa chừng.
        Assert.Equal(10_000m, row.ContractedFee);
        Assert.Equal(5_500m, row.RecognisedFee);
    }

    /// <summary>
    /// Lịch huỷ khi CHƯA ai trả đồng nào vẫn phải lên bảng — đây là ca admin đi tìm "booking
    /// tôi vừa huỷ đâu rồi" mà trước đó không có chỗ nào trả lời. Nó nằm ngoài cohort tính tiền
    /// (đưa vào GMV là thổi phồng), nên mọi cột tiền phải bằng 0 và không con số tổng nào đổi.
    /// </summary>
    [Fact]
    public async Task BookingTable_ListsCancelledBookingsThatNeverTookMoney()
    {
        await using var context = CreateContext();
        SeedCancelledCourse(context, deliveredSessions: 0, refunded: 0m, releasedToTutor: 0m, paid: false);
        await context.SaveChangesAsync();

        var service = CreateService(context);
        var data = await service.GetRecognitionAsync(From, To);
        var row = Assert.Single(data.BookingProgress);

        Assert.Equal(BookingStatus.CancelledByStaff, row.Status);
        Assert.Equal(0m, row.CashCollected);
        Assert.Equal(0m, row.RefundedAmount);
        Assert.Equal(0m, row.ContractedFee);
        Assert.Equal(0m, row.RecognisedFee);

        // Có mặt trên bảng nhưng KHÔNG được chạm vào con số nào của thẻ đầu trang.
        var s = data.Summary;
        Assert.Equal(0m, s.Gmv);
        Assert.Equal(0m, s.CommissionSold);
        Assert.Equal(0m, s.CommissionLost);
        Assert.Equal(0m, s.RecognisedRevenue);
    }

    /// <summary>
    /// Lịch quá hạn thanh toán cũng lên bảng (cùng nhóm lọc với huỷ), còn lịch CÒN ĐANG chờ thì
    /// không: nó chưa chết, chỉ chưa tới lượt — đưa vào chỉ làm bảng doanh thu đầy dòng rỗng.
    /// </summary>
    [Fact]
    public async Task BookingTable_KeepsPendingBookingsOutButListsTimeouts()
    {
        await using var context = CreateContext();
        SeedCancelledCourse(context, deliveredSessions: 0, refunded: 0m, releasedToTutor: 0m, paid: false);
        var booking = context.Bookings.Local.Single();
        booking.Status = BookingStatus.PaymentTimeout;
        booking.Cancelledat = null;
        booking.Escrowstatus = null;
        await context.SaveChangesAsync();

        var timeout = await CreateService(context).GetRecognitionAsync(From, To);
        Assert.Equal(BookingStatus.PaymentTimeout, Assert.Single(timeout.BookingProgress).Status);

        booking.Status = BookingStatus.PendingPayment;
        await context.SaveChangesAsync();

        var pending = await CreateService(context).GetRecognitionAsync(From, To);
        Assert.Empty(pending.BookingProgress);
    }

    /// <param name="deliveredSessions">Số buổi đã dạy xong và settle; phần còn lại là buổi huỷ.</param>
    /// <param name="paid">Phụ huynh đã trả đợt 1 chưa.</param>
    /// <param name="paidRemaining">Đã trả nốt đợt 2 chưa — quyết định `CashIn` là 105.000 hay 10.500.</param>
    private static void SeedCancelledCourse(
        AgoraDbContext context,
        int deliveredSessions = 1,
        decimal refunded = 90_000m,
        decimal releasedToTutor = 9_500m,
        bool paid = true,
        bool paidRemaining = true)
    {
        context.Users.AddRange(
            new User { Userid = TutorId, Fullname = "Gia sư A", Primaryrole = UserRole.Tutor, Password = "x" },
            new User { Userid = ParentId, Fullname = "Phụ huynh B", Primaryrole = UserRole.Parent, Password = "x" },
            new User { Userid = StudentId, Fullname = "Học sinh C", Primaryrole = UserRole.Student, Password = "x" });

        context.Tutorprofiles.Add(new Tutorprofile { Tutorid = TutorId });

        context.Bookings.Add(new Booking
        {
            Bookingid = BookingId,
            Parentid = ParentId,
            Studentid = StudentId,
            Tutorid = TutorId,
            Status = BookingStatus.CancelledByStaff,
            Totalsessions = 10,
            Totalamount = 100_000m,     // học phí gốc
            Parentfee = 5_000m,         // +5% phụ huynh trả thêm
            Finalprice = 105_000m,      // phụ huynh trả
            Tutorfee = 95_000m,         // gia sư nhận (đã trừ 5% phí gia sư)
            Platformfee = 10_000m,      // hoa hồng 10% trên học phí gốc
            Depositamount = 10_500m,    // đợt 1 = giá 1 buổi
            Escrowstatus = EscrowStatus.Refunded,
            Createdat = CreatedAt,
            Depositpaidat = paid ? CreatedAt : null,
            Remainingpaidat = paid && paidRemaining ? CreatedAt : null,
            Cancelledat = CancelledAt,
        });

        for (var i = 1; i <= 10; i++)
        {
            var delivered = i <= deliveredSessions;
            context.ClassSessions.Add(new ClassSession
            {
                Classsessionid = i,
                Bookingid = BookingId,
                Tutorid = TutorId,
                Status = delivered ? ClassSessionStatus.Completed : ClassSessionStatus.Cancelled,
                Issettled = delivered,
                Scheduledstart = DeliveredAt,
                Realend = DeliveredAt,
            });
        }

        // Ví: phải khớp đúng những gì SettlementService.CancelRemainingSessionsAsync ghi ra.
        var wallets = new[]
        {
            new Wallet { Walletid = 1, Userid = ParentId },
            new Wallet { Walletid = 2, Userid = TutorId },
        };
        context.Wallets.AddRange(wallets);

        if (refunded > 0)
        {
            context.Wallettransactions.Add(new Wallettransaction
            {
                Transactionid = 1,
                Walletid = 1,
                Amount = refunded,
                Transactiontype = TransactionType.Refund,
                Referencetable = ReferenceTable.Booking,
                Referenceid = BookingId,
                Createdat = CancelledAt,
            });
        }

        if (releasedToTutor > 0)
        {
            context.Wallettransactions.Add(new Wallettransaction
            {
                Transactionid = 2,
                Walletid = 2,
                Amount = releasedToTutor,
                Transactiontype = TransactionType.EscrowRelease,
                Referencetable = ReferenceTable.Booking,
                Referenceid = BookingId,
                Createdat = CancelledAt,
            });
        }

        // Escrow của 9 buổi chưa dạy bị ĐẢO chứ không giải ngân. Loại giao dịch khác nên không
        // được trừ vào phần Tutora giữ — seed vào đây đúng để test bắt được nếu ai đó gộp nhầm
        // EscrowReversal chung với EscrowRelease.
        context.Wallettransactions.Add(new Wallettransaction
        {
            Transactionid = 3,
            Walletid = 2,
            Amount = -85_500m,
            Transactiontype = TransactionType.EscrowReversal,
            Referencetable = ReferenceTable.Booking,
            Referenceid = BookingId,
            Createdat = CancelledAt,
        });
    }

    /// <summary>
    /// Booking #318 trên dev, dựng lại nguyên số: Hoá Học 9 buổi, học phí gốc 1.350.000đ, phụ
    /// huynh trả cọc đúng một buổi 157.500đ rồi chờ gia sư xác nhận.
    ///
    /// Báo cáo từng hiện "Đã thu được 135.000đ" cho dòng này — trọn phí sàn của cả 9 buổi, từ
    /// một khoá chưa dạy buổi nào. Con số đúng là 0: buổi đầu chưa diễn ra nên phụ huynh vẫn
    /// huỷ được và lấy lại đủ 157.500đ, kể cả 7.500đ phí dịch vụ.
    /// </summary>
    [Fact]
    public async Task Booking318_RecognisesNothing_BecauseTheFirstSessionHasNotHappened()
    {
        await using var context = CreateContext();
        context.Users.AddRange(
            new User { Userid = TutorId, Fullname = "Lê Gia Nam", Primaryrole = UserRole.Tutor, Password = "x" },
            new User { Userid = ParentId, Fullname = "Lê Nhật Nam", Primaryrole = UserRole.Parent, Password = "x" },
            new User { Userid = StudentId, Fullname = "Phạm Phương Nhi", Primaryrole = UserRole.Student, Password = "x" });
        context.Tutorprofiles.Add(new Tutorprofile { Tutorid = TutorId });
        context.Bookings.Add(new Booking
        {
            Bookingid = BookingId,
            Parentid = ParentId,
            Studentid = StudentId,
            Tutorid = TutorId,
            Status = BookingStatus.PendingTutor,
            Totalsessions = 9,
            Totalamount = 1_350_000m,
            Parentfee = 67_500m,
            Finalprice = 1_417_500m,
            Tutorfee = 1_282_500m,
            Platformfee = 135_000m,
            Depositamount = 157_500m,      // floor(1.417.500 / 9) = giá đúng 1 buổi
            Escrowstatus = EscrowStatus.Holding,
            Createdat = CreatedAt,
            Depositpaidat = CreatedAt,
            Remainingpaidat = null,
        });
        for (var i = 1; i <= 9; i++)
        {
            context.ClassSessions.Add(new ClassSession
            {
                Classsessionid = i,
                Bookingid = BookingId,
                Tutorid = TutorId,
                Status = ClassSessionStatus.Scheduled,
                Issettled = false,
                Scheduledstart = DeliveredAt,
            });
        }
        await context.SaveChangesAsync();

        var service = CreateService(context);
        var s = (await service.GetOverviewAsync(From, To)).Summary;

        Assert.Equal(0m, s.CommissionEarned);
        Assert.Equal(0m, s.RecognisedRevenue);
        Assert.Equal(135_000m, s.DeferredRevenue);      // toàn bộ phí sàn còn hoàn lại được
        Assert.Equal(0m, s.CommissionLost);
        Assert.Equal(135_000m, s.CommissionSold);

        var row = Assert.Single((await service.GetRecognitionAsync(From, To)).BookingProgress);
        Assert.Equal(157_500m, row.CashCollected);
        Assert.Equal(135_000m, row.ContractedFee);
        Assert.Equal(0m, row.RecognisedFee);
        Assert.Equal(0, row.DeliveredSessions);
    }

    private static AdminRevenueAnalyticsService CreateService(AgoraDbContext context) =>
        new(context, NullLogger<AdminRevenueAnalyticsService>.Instance);

    private static AgoraDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AgoraDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new RevenueTestDbContext(options);
    }

    private sealed class RevenueTestDbContext(DbContextOptions<AgoraDbContext> options)
        : AgoraDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            // pgvector columns have no in-memory equivalent.
            modelBuilder.Entity<QuestionBank>().Ignore(question => question.Embedding);
            modelBuilder.Entity<TutoraKbChunk>().Ignore(chunk => chunk.Embedding);
        }
    }
}
