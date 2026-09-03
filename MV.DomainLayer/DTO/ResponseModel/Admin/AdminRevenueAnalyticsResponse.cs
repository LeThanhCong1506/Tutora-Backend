namespace MV.DomainLayer.DTO.ResponseModel.Admin;

/// <summary>Số liệu cho bộ báo cáo doanh thu (/admin-portal/revenue-reports).</summary>
public class AdminRevenueOverviewResponse
{
    public RevenueSummaryDto Summary { get; set; } = new();
    public List<RevenueTrendPointDto> Trend { get; set; } = [];

    // RevenueMix và BookingFunnel đã bỏ 31/08/2026 — không màn hình nào đọc, mà endpoint này
    // giờ chạy ở cả trang chủ admin. Lý do đầy đủ trong GetOverviewAsync.
}

public class RevenueSummaryDto
{
    /// <summary>
    /// Doanh thu thật của kỳ: hoa hồng của mọi buổi đã dạy xong trong kỳ (neo theo ngày dạy),
    /// cộng phần chênh chốt tại ngày đóng sổ của các khoá đóng trong kỳ, cộng doanh thu AI.
    ///
    /// Phần chênh có thể ÂM — đó là ca hoàn tiền một phần theo khiếu nại, nơi Tutora giữ ít hơn
    /// hoa hồng của những buổi đã ghi nhận trước đó.
    /// </summary>
    public decimal RecognisedRevenue { get; set; }
    public decimal RecognisedPrevious { get; set; }

    /// <summary>Toàn bộ phí của booking tạo trong kỳ + doanh thu AI (cách cũ).</summary>
    public decimal ContractedRevenue { get; set; }
    public decimal ContractedPrevious { get; set; }

    /// <summary>Đã thu tiền nhưng buổi học chưa dạy — nghĩa vụ dịch vụ phải giao.</summary>
    public decimal DeferredRevenue { get; set; }
    public decimal DeferredPrevious { get; set; }

    /// <summary>
    /// Tổng GIÁ TRỊ LỊCH ĐẶT của kỳ — cộng theo giá hợp đồng, chốt lúc khách bấm đặt. KHÔNG
    /// phải doanh thu, và cũng KHÔNG phải tiền khách đã thực trả.
    ///
    /// Khách trả làm 2 đợt, nên khoá mới trả đợt 1 vẫn vào đây theo TRỌN giá gói. Đo trên dev
    /// 02/09/2026: 25/36 khoá của kỳ mới trả đợt 1, khiến con số này gấp ~3 lần tiền mặt thật
    /// (19.582.500 hợp đồng vs 6.520.500 đã trả). Phần đã thực trả nằm ở <see cref="GmvPaid"/> —
    /// luôn hiển thị kèm, nếu không cái nhãn sẽ hứa nhiều hơn con số.
    /// </summary>
    public decimal Gmv { get; set; }
    public decimal GmvPrevious { get; set; }

    /// <summary>
    /// Phần <see cref="Gmv"/> khách ĐÃ THỰC TRẢ: trả đủ 2 đợt thì tính trọn giá gói, mới trả
    /// đợt 1 thì chỉ tính tiền đợt 1. Cùng công thức <c>CashPaidIn</c> với sổ ví.
    ///
    /// CÙNG phạm vi với <see cref="Gmv"/> (booking cohort tạo trong kỳ) để hai số so trực tiếp
    /// được. KHÁC hẳn <see cref="CashCollected"/> — số kia neo theo NGÀY THANH TOÁN và không lọc
    /// cohort, nên đừng dùng thay thế.
    ///
    /// Chưa trừ hoàn tiền: khoản hoàn đã có thẻ riêng ở đầu trang.
    /// </summary>
    public decimal GmvPaid { get; set; }

    /// <summary>Tiền mặt thực thu trong kỳ (payment_transactions thành công).</summary>
    public decimal CashCollected { get; set; }
    public decimal CashPrevious { get; set; }

    // ── Bộ số dùng cho khối chia tiền ở tab Tổng quan ──────────────────────────────
    // Năm số dưới đây CÙNG một phạm vi: booking trong cohort (đang chạy, hoặc đã đóng
    // sổ mà phụ huynh đã thực trả tiền), tạo trong kỳ. Cùng phạm vi là điều kiện để
    // chúng cộng khớp — đây chính là thứ ba thẻ rời trước đây không làm được, khiến
    // người đọc tự cộng rồi thấy lệch.
    //
    //   Gmv = TutorReceivable + CommissionSold          (theo BookingFeeCalculator)
    //   CommissionSold = CommissionEarned + CommissionLost + phần còn chờ
    //
    // CommissionFromCancelled đứng NGOÀI hai đẳng thức trên: nó neo theo NGÀY HUỶ chứ
    // không phải ngày tạo booking, nên là số hạng của RecognisedRevenue, không phải
    // của khối chia tiền.

    /// <summary>
    /// Học phí gốc của booking tạo trong kỳ — MẪU SỐ của mọi tỉ lệ phí.
    ///
    /// Cần lộ ra vì hoa hồng 10% tính trên số này, không phải trên Gmv (Gmv đã cộng thêm 5%
    /// phí phụ huynh). Thiếu nó thì người đọc lấy hoa hồng chia Gmv sẽ ra 9,5% và tưởng hệ
    /// thống tính sai.
    /// </summary>
    public decimal BaseAmount { get; set; }

    /// <summary>Tiền gia sư nhận từ booking tạo trong kỳ (học phí gốc trừ 5% phí gia sư).</summary>
    public decimal TutorReceivable { get; set; }

    /// <summary>
    /// DOANH THU TẠM TÍNH: phí nền tảng 10% của booking tạo trong kỳ, chốt tại thời điểm đặt
    /// lịch. Không gồm doanh thu bán gói AI (khoản đó có tab riêng).
    ///
    /// Chưa phải tiền thật — buổi chưa dạy thì khoản này vẫn có thể mất. Phần đã thành tiền
    /// nằm ở <see cref="CommissionEarned"/>.
    /// </summary>
    public decimal CommissionSold { get; set; }

    /// <summary>
    /// Cùng định nghĩa với <see cref="CommissionSold"/> nhưng của kỳ liền trước cùng độ dài.
    ///
    /// Có mặt để dashboard tính được % thay đổi trên ĐÚNG con số nó đang hiển thị. Dùng
    /// <see cref="ContractedPrevious"/> thay thế sẽ lệch, vì con số đó đã cộng thêm tiền bán
    /// gói AI của kỳ trước.
    /// </summary>
    public decimal CommissionSoldPrevious { get; set; }

    /// <summary>
    /// Phần hoa hồng trên đã thành tiền thật tính tới cuối kỳ.
    ///
    /// Booking chưa chốt sổ: hoa hồng của buổi đã dạy xong và đã giải ngân. Booking đã chốt sổ:
    /// số tiền Tutora THỰC GIỮ theo sổ ví (tiền phụ huynh trả − hoàn lại phụ huynh − giải ngân
    /// cho gia sư), vì khoá dừng giữa chừng thì khoản giữ lại còn gồm cả phí dịch vụ không hoàn
    /// của những buổi bị huỷ, không suy được bằng công thức "đơn giá × số buổi".
    ///
    /// Với khoá hoàn tất bình thường, hai cách cho ra CÙNG một số.
    ///
    /// Từ 02/09/2026 con số này LẠI ĐƯỢC HIỂN THỊ, dưới tên <see cref="CommissionMatured"/>.
    /// </summary>
    public decimal CommissionEarned { get; set; }

    /// <summary>
    /// Hoa hồng đã ký nhưng VĨNH VIỄN không thu được: khoá bị huỷ, hoặc khách bỏ dở sau đợt 1
    /// rồi hệ thống đóng khoá.
    ///
    /// Tách hẳn khỏi phần "chờ ghi nhận" (<c>CommissionSold − CommissionEarned − CommissionLost</c>):
    /// chờ thì buổi học còn ở phía trước và vẫn có cơ hội thành tiền, còn khoản này thì hết cơ
    /// hội. Gộp chung sẽ làm nợ dịch vụ trông như vẫn còn thu được.
    ///
    /// ─── TẠM KHÔNG HIỂN THỊ từ 01/09/2026 — GIỮ LẠI ĐỂ ĐỐI SOÁT ────────────────────
    ///
    /// Vành khuyên "Số tạm tính đi về đâu" (ba lát Đã thu được / Còn chờ / Không thu được) đã
    /// bị gỡ khỏi tab Doanh thu. KHÔNG phải vì cách trình bày, mà vì cả ba lát đang sai số:
    /// chúng dựa vào <c>PlatformKept = cashIn − refunded − released</c>, mà <c>released</c>
    /// đang bị hụt do lỗi đảo escrow (escrow là túi chung theo ví gia sư — luồng huỷ đảo theo
    /// số buổi HỢP ĐỒNG nên rút vượt phần khoá đó thực nạp và ăn sang khoá khác). Released hụt
    /// ⇒ kept đội lên ⇒ <see cref="CommissionEarned"/> phồng và <see cref="CommissionLost"/>
    /// hụt. Đo trên dev 01/09/2026: 950.000 escrow bị đảo lố, 2 gia sư bị thiếu 665.000.
    ///
    /// Hai trường này CỐ Ý giữ lại (không xoá) vì sau khi sửa xong luồng escrow phải đo lại
    /// đúng chúng để quyết việc có nên tính khoá đã chốt sổ theo tiền thật thay vì giá hợp
    /// đồng hay không. Xoá đi rồi dựng lại là phí.
    ///
    /// Cảnh báo tự động cho lỗi trên nằm ở <c>AdminRevenueAnalyticsService.BuildClosedBookings</c>
    /// (LogWarning khi phép chặn phải cắt số).
    /// </summary>
    public decimal CommissionLost { get; set; }

    // ── Ba số phận của DOANH THU TẠM TÍNH — thêm 02/09/2026 ───────────────────────
    //
    //   CommissionSold = CommissionMatured + CommissionPending + CommissionUnrecoverable
    //
    // Khít theo construction ở MỌI kỳ: mỗi khoá đóng góp đúng `PlatformFee` của nó, tách làm hai
    // nhánh rời nhau — đã chốt sổ thì `kept + (PlatformFee − kept)`, chưa chốt thì
    // `earned + (PlatformFee − earned)`. Không khoá nào rơi vào cả hai nhánh.
    //
    // ─── Sửa 02/09/2026 (trong ngày): bộ ba GIỜ ĐỌC SỔ VÍ ────────────────────────
    //
    // Bản đầu của bộ ba cố ý KHÔNG chạm sổ ví, chỉ dùng công thức, để miễn nhiễm với lỗi đảo
    // escrow. Đã đảo lại quyết định đó ngay trong ngày, vì nó không đạt mục tiêu:
    //
    //   `RecognisedRevenue` — con số kế toán lớn nhất trên màn hình — VẪN cộng
    //   `ClosingAdjustmentIn`, mà khoản đó suy thẳng từ `PlatformKept`, tức từ sổ ví. Nên trang
    //   chạy hai chính sách tin cậy khác nhau và in ra hai con số cho cùng một ý niệm:
    //   461.000 ở thẻ doanh thu, 458.500 ở bộ ba (dev, 02/09/2026). Không đọc ví ở đây KHÔNG làm
    //   trang an toàn hơn — số rủi ro vẫn nằm nguyên trên màn hình — chỉ làm hai chỗ mâu thuẫn.
    //
    // `CommissionMatured` và `CommissionUnrecoverable` do đó bằng ĐÚNG `CommissionEarned` và
    // `CommissionLost`. Giữ cả hai bộ tên vì mỗi bộ nói một câu khác nhau ở tài liệu và giao
    // diện; đừng "dọn" bằng cách xoá một bộ.
    //
    // Phần bảo vệ thật thì không mất, nó nằm ở `BuildClosedBookings`: chặn `ledgerTouched` (khoá
    // chưa có dòng ví nào thì dùng công thức, đừng đoán qua ví — chặn này một mình đã khử 320.000
    // doanh thu ảo khi thêm dấu hiệu chốt sổ thứ ba) và chặn trên ở `PlatformFee`.
    //
    // Bộ ba vẫn tái lập ĐÚNG ví dụ chuẩn của tài liệu (§2.1): khoá 100k/10 buổi, phụ huynh trả
    // đủ, học 1 buổi rồi huỷ → matured = 5.500, đúng bằng "Tutora giữ 5.500 = 1.000 hoa hồng buổi
    // đã dạy + 4.500 phí dịch vụ không hoàn". Công thức ngây thơ "hoa hồng × buổi đã dạy" chỉ ra
    // 1.000 — nó bỏ sót phần phí dịch vụ không hoàn.
    //
    // Cả ba neo theo NGÀY ĐẶT LỊCH (booking tạo trong kỳ), khác mốc với RecognisedRevenue vốn neo
    // theo ngày dạy. Sau khi sửa, `CommissionMatured` trùng đúng phần "dạy học" của
    // RecognisedRevenue ở mọi kỳ ≥ 21 ngày trên dữ liệu dev; kỳ ngắn hơn vẫn lệch, và lệch ĐÚNG —
    // buổi dạy trong kỳ có thể thuộc khoá đặt từ trước kỳ. Vẫn KHÔNG cộng chéo hai bộ.

    /// <summary>
    /// Tiền bán gói AI trong kỳ, neo theo ngày thanh toán. ĐÃ nằm sẵn trong
    /// <see cref="RecognisedRevenue"/> — lộ riêng ra để giao diện tách được doanh thu theo NGUỒN:
    ///
    ///   RecognisedRevenue = (phí gia sư + phí phụ huynh từ buổi dạy) + AiRevenue
    ///
    /// Hai vế cộng KHÍT TUYỆT ĐỐI vì cùng neo ngày ghi nhận, nên vế trái suy ngược ra bằng
    /// hiệu chứ không cần thêm trường. Hai nguồn khác hẳn bản chất — một khoản chỉ chín khi có
    /// buổi dạy xong và đi qua escrow, một khoản thu đứt ngay lúc mua — nên gộp làm một con số
    /// mà không ghi rõ là để người đọc tự đoán sai tỉ lệ.
    ///
    /// KHÔNG cộng nó với <see cref="CommissionMatured"/>: số kia neo NGÀY ĐẶT LỊCH, khác mốc,
    /// nên phép cộng luôn thừa ra một phần dư phải bịa tên. Giao diện đã thử một bản như vậy
    /// ("dòng nối") và đã gỡ ngay trong ngày 02/09/2026 vì rối.
    /// </summary>
    public decimal AiRevenue { get; set; }

    /// <summary>
    /// Phí sàn đã thu được tính tới cuối kỳ, của các lịch đặt trong kỳ. Nhãn trên giao diện:
    /// "Đã thu được" (quy ước thuật ngữ chốt 31/08/2026).
    ///
    /// Bằng ĐÚNG <see cref="CommissionEarned"/> từ 02/09/2026 — xem ghi chú của cụm ba số phận
    /// ở trên về việc vì sao bỏ bản tính bằng công thức thuần.
    /// </summary>
    public decimal CommissionMatured { get; set; }

    /// <summary>Phí sàn còn CƠ HỘI chín: khoá vẫn đang chạy, buổi còn ở phía trước.</summary>
    public decimal CommissionPending { get; set; }

    /// <summary>
    /// Phí sàn VĨNH VIỄN không thu được: khoá đã chốt sổ mà phần này chưa kịp chín — buổi bị huỷ
    /// nên không có gì để cắt, hoặc khách không trả nốt nên phí dịch vụ không bao giờ tới.
    ///
    /// Tách hẳn khỏi <see cref="CommissionPending"/>: chờ nghĩa là tiền còn cơ hội về, khoản này
    /// thì hết. Gộp chung là báo một khoản đã chết như thể vẫn đang chờ — và con số đó sẽ không
    /// bao giờ giảm qua các kỳ, thứ ai đối chiếu hai kỳ liên tiếp cũng phát hiện ra.
    ///
    /// Bằng ĐÚNG <see cref="CommissionLost"/> từ 02/09/2026 — xem ghi chú của cụm ba số phận.
    /// </summary>
    public decimal CommissionUnrecoverable { get; set; }

    /// <summary>
    /// Đối soát sổ ví: tổng tiền Tutora giữ được từ các khoá bị HUỶ đóng sổ trong kỳ.
    ///
    /// KHÔNG phải số hạng của <see cref="RecognisedRevenue"/>, và KHÔNG cộng được vào
    /// <see cref="CommissionEarned"/>. Lý do: hoa hồng của những buổi đã dạy trong khoá đó có
    /// thể đã được ghi nhận từ kỳ TRƯỚC (theo ngày dạy); trường này là số luỹ kế cả đời khoá,
    /// gộp vào bất kỳ tổng nào cũng là tính hai lần.
    ///
    /// Dùng để trả lời đúng một câu hỏi: "kỳ này huỷ nhiều thế, rốt cuộc giữ lại được bao nhiêu".
    /// </summary>
    public decimal CommissionFromCancelled { get; set; }

    /// <summary>
    /// Các MỨC PHÍ SÀN thực sự có mặt trong kỳ, kèm số tiền nằm ở mỗi mức. Sắp giảm dần theo
    /// <see cref="RevenueRateMixDto.BaseAmount"/>. Một phần tử = kỳ chỉ chạy một mức.
    /// </summary>
    /// <remarks>
    /// Thêm 03/09/2026 vì con số "tỉ lệ phí của kỳ" là một TRUNG BÌNH CÓ TRỌNG SỐ, và khi trong
    /// kỳ có nhiều mức thì nó không trùng mức nào cả — đo thật trên dev: 5,5% trong khi ba mức
    /// đang chạy là 5%, 10% và 20%. Không ai giải thích được con số 5,5% vì không booking nào
    /// từng bị tính mức đó.
    /// <para>
    /// Cách chữa KHÔNG phải là bỏ số lẻ hay đổi cách làm tròn, mà là thôi in ra một con số
    /// tổng hợp và nói thẳng thành phần. Nói "mức liền trước là bao nhiêu" cũng không đủ:
    /// bản trước lấy mức của lần đổi gần nhất làm đại diện cho toàn bộ lịch cũ, trong khi
    /// 94% học phí gốc của kỳ vẫn nằm ở mức từ hai lần đổi trước đó.
    /// </para>
    /// </remarks>
    public List<RevenueRateMixDto> RateMix { get; set; } = [];
}

/// <summary>Một mức phí sàn và phần tiền của kỳ đang nằm ở mức đó.</summary>
public class RevenueRateMixDto
{
    public decimal ParentFeePercent { get; set; }
    public decimal TutorFeePercent { get; set; }
    /// <summary>Học phí gốc của nhóm — mẫu số mà hai tỉ lệ trên tính trên đó.</summary>
    public decimal BaseAmount { get; set; }
    /// <summary>Phí sàn hai vế cộng lại của nhóm.</summary>
    public decimal Fee { get; set; }
    public int Bookings { get; set; }
}

public class RevenueTrendPointDto
{
    public string Month { get; set; } = "";

    /// <summary>
    /// Mốc BẮT ĐẦU của khoảng mà điểm này gộp, UTC.
    /// </summary>
    /// <remarks>
    /// Thêm 03/09/2026 để giao diện gắn được vạch mốc lên biểu đồ xu hướng — cụ thể là vạch
    /// "admin đổi mức phí sàn ngày X".
    /// <para>
    /// Không suy ngược từ <see cref="Month"/> được: nhãn đó có ba dạng tuỳ độ dài kỳ
    /// (<c>dd/MM</c> theo ngày, <c>dd/MM – dd/MM</c> theo tuần, <c>MM/yyyy</c> theo tháng), và
    /// hai dạng đầu KHÔNG mang năm nên kỳ bắc qua giao thừa là phân giải sai. Giao diện tự dựng
    /// lại luật chia mốc của <c>TimeBuckets</c> cũng không ổn: hai bản sẽ trôi khỏi nhau lặng lẽ,
    /// và triệu chứng là vạch mốc lệch vài ô chứ không phải một lỗi nhìn thấy được.
    /// </para>
    /// </remarks>
    public DateTime Start { get; set; }

    public decimal Recognised { get; set; }
    public decimal Contracted { get; set; }
    public decimal AiRevenue { get; set; }
    public decimal Gmv { get; set; }
}

/// <summary>Cặp tên–giá trị cho biểu đồ tròn. Chỉ còn dùng ở <c>Concentration</c> của tab Gia sư.</summary>
public class NamedValueDto
{
    public string Name { get; set; } = "";
    public decimal Value { get; set; }
}

/// <summary>Hoàn tiền trong kỳ. Nguồn: wallet_transactions type=Refund.</summary>
public class RefundStatsDto
{
    public decimal Amount { get; set; }
    public decimal AmountPrevious { get; set; }
    public int Count { get; set; }
    public int CountPrevious { get; set; }

    /// <summary>% trên tiền mặt đã thu trong kỳ.</summary>
    public decimal RateOfCash { get; set; }
}

public class RefundTrendPointDto
{
    public string Month { get; set; } = "";
    public decimal Amount { get; set; }
    public int Count { get; set; }
}

/// <summary>Trả đợt 1 nhưng chưa học buổi nào — khác <see cref="StalledBookingStatsDto"/>.</summary>
public class NeverStartedStatsDto
{
    public int Count { get; set; }
    public int CountPrevious { get; set; }

    /// <summary>Hoa hồng của số buổi đã bán.</summary>
    public decimal FeeAtRisk { get; set; }

    /// <summary>Tiền khách đã trả (GMV).</summary>
    public decimal CashHeld { get; set; }
}

public class AdminRevenueRecognitionResponse
{
    public RevenueSummaryDto Summary { get; set; } = new();
    public List<DeferredAgingDto> DeferredAging { get; set; } = [];
    public StalledBookingStatsDto Stalled { get; set; } = new();
    public NeverStartedStatsDto NeverStarted { get; set; } = new();
    public List<StalledTrendPointDto> StalledTrend { get; set; } = [];
    public RefundStatsDto Refunds { get; set; } = new();
    public List<RefundTrendPointDto> RefundTrend { get; set; } = [];
    public List<BookingProgressDto> BookingProgress { get; set; } = [];
}

public class DeferredAgingDto
{
    public string Bucket { get; set; } = "";
    public decimal Amount { get; set; }
    public int Bookings { get; set; }
}

/// <summary>Booking dừng ở deposit_paid quá hạn trả đợt 2 — rò rỉ doanh thu.</summary>
public class StalledBookingStatsDto
{
    public int Count { get; set; }
    public int CountPrevious { get; set; }
    public decimal ContractedFeeAtRisk { get; set; }
    public decimal DropOffRate { get; set; }
    public decimal DropOffPrevious { get; set; }
}

public class StalledTrendPointDto
{
    public string Month { get; set; } = "";
    public int Stalled { get; set; }
    public int Converted { get; set; }
}

public class BookingProgressDto
{
    public int BookingId { get; set; }
    public string ParentName { get; set; } = "";
    public string TutorName { get; set; } = "";

    /// <summary>
    /// Số điện thoại (hoặc email nếu không có số) của khách và của gia sư — CHỈ để phân biệt
    /// người trùng tên. Hai trường, không phải một, vì mỗi dòng ở đây có HAI người. Giao diện
    /// chỉ in ra khi tên thật sự bị trùng trong cùng một bảng.
    /// </summary>
    public string? ParentContact { get; set; }

    /// <inheritdoc cref="ParentContact"/>
    public string? TutorContact { get; set; }

    public string Subject { get; set; } = "";
    public int TotalSessions { get; set; }
    public int DeliveredSessions { get; set; }

    /// <summary>
    /// DOANH THU TẠM TÍNH của booking: toàn bộ phí nền tảng chốt lúc đặt lịch, gồm phí phụ
    /// huynh cộng phí sàn gia sư. Muốn xem tách 2 nguồn thì mở trang chi tiết booking
    /// (CMS: /admin-portal/bookings/:id).
    ///
    /// Bằng 0 với những lịch chết mà chưa có đồng nào chạy qua — chúng nằm ngoài cohort nên
    /// không được góp vào bất kỳ tổng tiền nào.
    ///
    /// Trước 31/08/2026 trường này trả về số THỰC GIỮ với khoá đã đóng sổ, khiến tổng cột hụt
    /// đúng phần "Không thu được" so với thẻ đầu trang. Đừng đưa nhánh đó quay lại.
    /// </summary>
    public decimal ContractedFee { get; set; }

    /// <summary>
    /// Phần đã THU ĐƯỢC của số tạm tính trên. Khoá đang chạy: phí/buổi × số buổi đã quyết
    /// toán. Khoá đã đóng sổ: số Tutora thực giữ theo sổ ví.
    ///
    /// Hiệu <c>ContractedFee − RecognisedFee</c> là phần còn chờ dạy (khoá đang chạy) hoặc
    /// mất hẳn (khoá đã đóng sổ) — phân biệt bằng cờ <see cref="Closed"/>.
    /// </summary>
    public decimal RecognisedFee { get; set; }

    /// <summary>
    /// Tiền phụ huynh đã thực trả vào booking này (đợt 1, hoặc cả hai đợt).
    ///
    /// Khác <c>FinalPrice</c>: khoá mới trả đợt 1 thì đây chỉ là tiền đợt 1. Có cột này thì
    /// dòng của một khoá đã huỷ mới đọc được — "khách trả bao nhiêu, hoàn lại bao nhiêu, Tutora
    /// giữ bao nhiêu" nằm trọn trên một dòng, không phải mở sang trang chi tiết.
    /// </summary>
    public decimal CashCollected { get; set; }

    /// <summary>Đã hoàn lại cho phụ huynh, lấy từ sổ ví (<c>wallet_transactions</c>).</summary>
    public decimal RefundedAmount { get; set; }

    /// <summary>
    /// Khoá đã chốt sổ — không còn đồng nào chảy vào hay ra nữa.
    ///
    /// Giao diện cần cờ này chứ không suy từ <see cref="Status"/> được: hai luồng đóng khoá
    /// giữa chừng (gia sư bị đình chỉ, khách bỏ dở sau đợt 1) vẫn để status <c>completed</c>,
    /// mà escrow thì đã chốt. Không có cờ thì bộ lọc "đã đóng" sẽ bỏ sót đúng nhóm đó.
    /// </summary>
    public bool Closed { get; set; }

    public DateTime? CreatedAt { get; set; }
    public string Status { get; set; } = "";
}

public class AdminTutorRevenueResponse
{
    /// <summary>Đã cắt còn <c>top</c> dòng — đừng dùng Count làm số liệu.</summary>
    public List<TutorRevenueDto> Tutors { get; set; } = [];

    /// <summary>Số gia sư dạy xong ít nhất một buổi trong kỳ.</summary>
    public int TutorsWithRevenue { get; set; }

    /// <summary>Số gia sư có buổi trong kỳ, kể cả huỷ hết.</summary>
    public int ActiveTutors { get; set; }

    public List<NamedValueDto> Concentration { get; set; } = [];
    /// <summary>Tổng doanh thu đến từ phí gia sư trong kỳ — KHÔNG gồm phí dịch vụ phụ huynh
    /// (xem <see cref="TutorRevenueDto.TutorFeeRevenue"/>).</summary>
    public decimal TotalTutorFeeRevenue { get; set; }

    /// <summary>
    /// Tổng phí gia sư còn ĐỢI ghi nhận — buổi đã bán mà chưa dạy nên chưa cắt được 5%.
    /// Đối xứng với <c>CustomerRevenueSummaryDto.ServiceFeePending</c> của tab Khách hàng.
    /// Thêm 02/09/2026: trước đó tab Gia sư chỉ có vế đã ghi nhận nên hai tab không đọc được
    /// như một cặp, dù chúng là hai nửa của cùng một phí sàn 10%.
    /// </summary>
    public decimal TotalTutorFeePending { get; set; }

    /// <summary>Escrow toàn sàn hiện tại — nợ phải trả, KHÔNG lọc theo kỳ.</summary>
    public decimal TotalEscrowHeld { get; set; }
}

public class TutorRevenueDto
{
    public string TutorId { get; set; } = "";
    public string TutorName { get; set; } = "";

    /// <summary>
    /// Số điện thoại (hoặc email nếu không có số) — CHỈ để phân biệt người trùng tên. Giao
    /// diện chỉ in nó ra khi tên thật sự bị trùng trong cùng một bảng. <c>null</c> khi tài
    /// khoản không có gì phân biệt được.
    /// </summary>
    public string? Contact { get; set; }
    public string Subject { get; set; } = "";
    public decimal Gmv { get; set; }

    /// <summary>
    /// Doanh thu nền tảng đến TỪ GIA SƯ này: 5% cắt từ <c>Tutorfee</c> của các buổi họ đã dạy
    /// trong kỳ. KHÔNG gồm 5% phí dịch vụ phụ huynh trả — nửa đó thuộc tab Khách hàng, nên
    /// tổng tab Gia sư cố ý nhỏ hơn "Doanh thu đã ghi nhận" của tab Doanh thu.
    /// </summary>
    public decimal TutorFeeRevenue { get; set; }

    /// <summary>
    /// Phí gia sư của người này còn ĐỢI ghi nhận: phần 5% của những buổi đã bán mà chưa dạy.
    /// Khoá đã chốt sổ ra 0 — buổi chưa dạy của chúng đã bị huỷ, không còn gì để chờ.
    /// Đối xứng với cột "Phí DV đợi ghi nhận" của bảng khách hàng.
    /// </summary>
    public decimal TutorFeePending { get; set; }

    /// <summary>% giữ lại trên GMV. Chỉ so sánh tương đối — hai vế khác mốc thời gian.</summary>
    public decimal TakeRate { get; set; }

    public decimal TutorEarnings { get; set; }
    public decimal EscrowHeld { get; set; }
    public int SessionsDelivered { get; set; }
    public decimal RevenuePerSession { get; set; }
    public decimal CancelRate { get; set; }
    public int DisputeCount { get; set; }
    public decimal Rating { get; set; }
}

public class AdminCustomerRevenueResponse
{
    public CustomerSummaryDto Summary { get; set; } = new();

    /// <summary>Phân khúc người chi tiền: phụ huynh vs học sinh tự đặt.</summary>
    public List<CustomerSegmentDto> Segments { get; set; } = [];

    public List<ParentRevenueDto> Parents { get; set; } = [];
    public List<ArpuPointDto> ArpuTrend { get; set; } = [];
    public List<NewVsReturningDto> NewVsReturning { get; set; } = [];
    public List<NamedCountDto> BookingValueDistribution { get; set; } = [];
    public List<CohortRowDto> Cohorts { get; set; } = [];
}

/// <summary>Phân khúc khách. Số liệu trong kỳ, trừ Ltv/RepeatRate tính toàn lịch sử.</summary>
public class CustomerSegmentDto
{
    public string Segment { get; set; } = "";

    /// <summary>Số khách khác nhau có booking trong kỳ.</summary>
    public int Customers { get; set; }

    public int Bookings { get; set; }

    /// <summary>Tiền khách trả (GMV), không phải hoa hồng.</summary>
    public decimal TotalSpent { get; set; }

    /// <summary>Phí dịch vụ 5% ĐÃ ghi nhận từ nhóm này — khoá đã qua buổi đầu.</summary>
    public decimal ServiceFeeRecognised { get; set; }

    /// <summary>Phí dịch vụ 5% còn ĐỢI ghi nhận: chưa trả, hoặc đã trả mà chưa qua buổi đầu
    /// nên vẫn hoàn lại được 100%.</summary>
    public decimal ServiceFeePending { get; set; }

    /// <summary>Chi tiêu bình quân mỗi khách, toàn lịch sử.</summary>
    public decimal Ltv { get; set; }

    public decimal AvgBookingValue { get; set; }

    /// <summary>% khách đặt từ 2 booking trở lên.</summary>
    public decimal RepeatRate { get; set; }
}

public class CustomerSummaryDto
{
    /// <summary>Phí dịch vụ 5% ĐÃ ghi nhận từ các lịch đặt trong kỳ — tiền thật.</summary>
    public decimal ServiceFeeRecognised { get; set; }

    /// <summary>Phí dịch vụ 5% còn ĐỢI ghi nhận từ các lịch đặt trong kỳ.</summary>
    public decimal ServiceFeePending { get; set; }

    public int ActiveParents { get; set; }
    public decimal RepeatRate { get; set; }
    public decimal RepeatRatePrevious { get; set; }
    public decimal AvgBookingValue { get; set; }
    public decimal AvgBookingValuePrevious { get; set; }
    public decimal Ltv { get; set; }
}

public class ParentRevenueDto
{
    /// <summary>Phụ huynh, hoặc học sinh nếu tự đặt lịch.</summary>
    public string ParentId { get; set; } = "";
    public string ParentName { get; set; } = "";

    /// <summary>
    /// Số điện thoại (hoặc email nếu không có số) — CHỈ để phân biệt người trùng tên. Giao
    /// diện chỉ in nó ra khi tên thật sự bị trùng trong cùng một bảng. <c>null</c> khi tài
    /// khoản không có gì phân biệt được.
    /// </summary>
    public string? Contact { get; set; }

    /// <summary>"Phụ huynh" hoặc "Học sinh".</summary>
    public string CustomerType { get; set; } = "";

    public string StudentName { get; set; } = "";
    public decimal TotalSpent { get; set; }
    public int BookingCount { get; set; }
    public int SessionsPurchased { get; set; }
    public int SessionsCompleted { get; set; }

    /// <summary>Phí dịch vụ 5% khách này đã trả và ĐÃ ghi nhận (khoá đã qua buổi đầu).</summary>
    public decimal ServiceFeeRecognised { get; set; }

    /// <summary>Phí dịch vụ 5% còn ĐỢI ghi nhận. Cộng với trường trên đúng bằng tổng
    /// `Parentfee` các khoá của khách. KHÔNG suy ra từ TotalSpent (số đó gồm phần gia sư).</summary>
    public decimal ServiceFeePending { get; set; }
    public DateTime? FirstBookingAt { get; set; }
    public DateTime? LastBookingAt { get; set; }
}

public class ArpuPointDto
{
    public string Month { get; set; } = "";
    public decimal Arpu { get; set; }
    public int ActiveParents { get; set; }
}

public class NewVsReturningDto
{
    public string Month { get; set; } = "";
    public int NewCustomers { get; set; }
    public int Returning { get; set; }
}

public class NamedCountDto
{
    public string Range { get; set; } = "";
    public int Count { get; set; }
}

public class CohortRowDto
{
    public string Cohort { get; set; } = "";
    public int Size { get; set; }
    /// <summary>% còn hoạt động ở tháng thứ 0..N; null = chưa tới kỳ đó.</summary>
    public List<decimal?> Retention { get; set; } = [];
}

public class AdminSubjectRevenueResponse
{
    public List<SubjectRevenueDto> Subjects { get; set; } = [];
    public List<GradeRevenueDto> Grades { get; set; } = [];
    public List<SubjectGradeCellDto> Matrix { get; set; } = [];
    public List<Dictionary<string, object>> SubjectTrend { get; set; } = [];
}

public class SubjectRevenueDto
{
    public int SubjectId { get; set; }
    public string SubjectName { get; set; } = "";
    public decimal Gmv { get; set; }
    public decimal PlatformRevenue { get; set; }

    /// <summary>Hoa hồng buổi chưa dạy. KHÔNG phải Gmv − PlatformRevenue (khác cơ sở).</summary>
    public decimal DeferredRevenue { get; set; }

    public int Bookings { get; set; }
    public int SessionsDelivered { get; set; }
    public decimal AvgPricePerSession { get; set; }
    public decimal CompletionRate { get; set; }
}

public class GradeRevenueDto
{
    public int GradeId { get; set; }
    public string GradeName { get; set; } = "";
    public decimal Gmv { get; set; }
    public decimal PlatformRevenue { get; set; }
    public int Bookings { get; set; }
}

public class SubjectGradeCellDto
{
    public string Subject { get; set; } = "";
    public string Grade { get; set; } = "";
    public decimal Revenue { get; set; }
}

public class AdminAiRevenueResponse
{
    public AiSummaryDto Summary { get; set; } = new();
    public List<AiPackageDto> Packages { get; set; } = [];
    public List<AiCreditFlowDto> CreditFlow { get; set; } = [];
    public List<AiTopUserDto> TopUsers { get; set; } = [];
    public List<RevenueTrendPointDto> Trend { get; set; } = [];
}

public class AiSummaryDto
{
    public decimal Revenue { get; set; }
    public decimal RevenuePrevious { get; set; }
    public int PackagesSold { get; set; }
    public int PackagesSoldPrevious { get; set; }
    /// <summary>Tổng credit đã cấp (tặng Free + mua gói), luỹ kế toàn hệ thống.</summary>
    public int CreditsSold { get; set; }

    /// <summary>Tổng lượt đã hỏi, cộng từ ai_usage_monthly.</summary>
    public int CreditsConsumed { get; set; }

    /// <summary>Credit còn lại — không quy ra tiền được vì mỗi gói một đơn giá.</summary>
    public int CreditsOutstanding { get; set; }

    // Tách độ phủ (TotalUsers/ActivatedUsers) khỏi cường độ (ActivatedCredits*): mọi
    // tài khoản đều được tặng lượt nên chia trên tổng sẽ ra tỷ lệ ~1% vô nghĩa.

    /// <summary>Số tài khoản được cấp lượt AI.</summary>
    public int TotalUsers { get; set; }

    /// <summary>Số tài khoản đã hỏi ít nhất một lượt.</summary>
    public int ActivatedUsers { get; set; }

    /// <summary>Lượt đã cấp cho riêng nhóm đã kích hoạt.</summary>
    public int ActivatedCreditsGranted { get; set; }

    /// <summary>Lượt đã dùng của riêng nhóm đã kích hoạt.</summary>
    public int ActivatedCreditsConsumed { get; set; }
}

public class AiPackageDto
{
    public int PackageId { get; set; }
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
    public int CreditAmount { get; set; }
    public int UnitsSold { get; set; }
    public decimal Revenue { get; set; }
}

/// <summary>Lượt AI được cấp và được dùng trong tháng.</summary>
public class AiCreditFlowDto
{
    public string Month { get; set; } = "";

    /// <summary>Lượt cấp trong tháng, chỉ tính tài khoản đã từng hỏi bài.</summary>
    public int Granted { get; set; }

    public int Consumed { get; set; }
}

public class AiTopUserDto
{
    public string UserId { get; set; } = "";
    public string UserName { get; set; } = "";

    /// <summary>
    /// Số điện thoại (hoặc email nếu không có số) — CHỈ để phân biệt người trùng tên. Giao
    /// diện chỉ in nó ra khi tên thật sự bị trùng trong cùng một bảng. <c>null</c> khi tài
    /// khoản không có gì phân biệt được.
    /// </summary>
    public string? Contact { get; set; }
    public string Role { get; set; } = "";
    public int CreditsConsumed { get; set; }
    public int CreditsPurchased { get; set; }
    public decimal AmountPaid { get; set; }
}
