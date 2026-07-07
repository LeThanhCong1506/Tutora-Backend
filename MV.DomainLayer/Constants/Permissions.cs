namespace MV.DomainLayer.Constants;

public static class Permissions
{
    // Tutor onboarding & certificates
    public const string TutorApprovalView    = "tutor_approval.view";
    public const string TutorApprovalDecide  = "tutor_approval.decide";
    public const string CertificateView      = "certificate.view";
    public const string CertificateVerify    = "certificate.verify";
    public const string TutorCccdView        = "tutor_cccd.view";

    // User management (KHONG bao gom ChangeUserRole/DeleteUser - luon Admin-only)
    public const string UserView             = "user.view";
    public const string UserUpdate           = "user.update";
    public const string UserDeactivate       = "user.deactivate";

    public const string DashboardView        = "dashboard.view";
    public const string FinancialView        = "financial.view";
    public const string BookingView          = "booking.view";
    public const string PromotionManage      = "promotion.manage";

    public const string PayoutView           = "payout.view";
    public const string PayoutApprove        = "payout.approve";
    public const string PayoutReject         = "payout.reject";
    public const string PayoutBalanceView    = "payout.balance_view";
    public const string FraudLogView         = "fraud_log.view";
    public const string SystemAlertView      = "system_alert.view";
    public const string SystemAlertResolve   = "system_alert.resolve";

    public const string DisputeView          = "dispute.view";
    public const string DisputeInvestigate   = "dispute.investigate";
    public const string DisputeResolve       = "dispute.resolve";

    public const string WarningCreate        = "warning.create";
    public const string WarningView          = "warning.view";
    public const string SuspensionManage     = "suspension.manage";

    public const string ExportData           = "export.data";

    public const string NotificationSend     = "notification.send";
    public const string NotificationView     = "notification.view";
    public const string NotificationDelete   = "notification.delete";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        TutorApprovalView, TutorApprovalDecide, CertificateView, CertificateVerify, TutorCccdView,
        UserView, UserUpdate, UserDeactivate,
        DashboardView, FinancialView, BookingView, PromotionManage,
        PayoutView, PayoutApprove, PayoutReject, PayoutBalanceView, FraudLogView,
        SystemAlertView, SystemAlertResolve,
        DisputeView, DisputeInvestigate, DisputeResolve,
        WarningCreate, WarningView, SuspensionManage,
        ExportData,
        NotificationSend, NotificationView, NotificationDelete
    };

    /// <summary>Catalog kem nhan tieng Viet + nhom chuc nang, dung render checkbox UI cho Admin.</summary>
    public static readonly IReadOnlyList<PermissionDescriptor> Catalog = new List<PermissionDescriptor>
    {
        new(TutorApprovalView,   "Xem danh sách gia sư chờ duyệt",      "Tutor Onboarding"),
        new(TutorApprovalDecide, "Duyệt / từ chối hồ sơ gia sư",        "Tutor Onboarding"),
        new(CertificateView,     "Xem chứng chỉ chờ duyệt",             "Tutor Onboarding"),
        new(CertificateVerify,   "Xác minh chứng chỉ gia sư",           "Tutor Onboarding"),
        new(TutorCccdView,       "Xem ảnh CCCD gia sư",                 "Tutor Onboarding"),
        new(UserView,            "Xem danh sách / chi tiết người dùng", "User Management"),
        new(UserUpdate,          "Chỉnh sửa thông tin người dùng",      "User Management"),
        new(UserDeactivate,      "Khóa / mở khóa tài khoản",            "User Management"),
        new(DashboardView,       "Xem dashboard & báo cáo thống kê",    "Reporting"),
        new(FinancialView,       "Xem báo cáo tài chính",               "Reporting"),
        new(BookingView,         "Xem danh sách / chi tiết booking",    "Booking"),
        new(PromotionManage,     "Quản lý mã khuyến mãi",               "Booking"),
        new(PayoutView,          "Xem yêu cầu rút tiền",                "Payout"),
        new(PayoutApprove,       "Duyệt yêu cầu rút tiền",              "Payout"),
        new(PayoutReject,        "Từ chối yêu cầu rút tiền",            "Payout"),
        new(PayoutBalanceView,   "Xem số dư PayOS",                     "Payout"),
        new(FraudLogView,        "Xem nhật ký gian lận (lịch sử)",      "Payout"),
        new(SystemAlertView,     "Xem cảnh báo hệ thống",               "Payout"),
        new(SystemAlertResolve,  "Xử lý cảnh báo hệ thống",             "Payout"),
        new(DisputeView,         "Xem tranh chấp",                      "Dispute"),
        new(DisputeInvestigate,  "Điều tra tranh chấp",                 "Dispute"),
        new(DisputeResolve,      "Giải quyết tranh chấp",               "Dispute"),
        new(WarningCreate,       "Tạo cảnh cáo người dùng",             "Warning & Suspension"),
        new(WarningView,         "Xem lịch sử cảnh cáo / đình chỉ",     "Warning & Suspension"),
        new(SuspensionManage,    "Đình chỉ / gỡ đình chỉ tài khoản",    "Warning & Suspension"),
        new(ExportData,          "Xuất dữ liệu Excel",                  "Export"),
        new(NotificationSend,    "Gửi thông báo (đơn / hàng loạt)",     "Notification"),
        new(NotificationView,    "Xem thông báo hệ thống",              "Notification"),
        new(NotificationDelete,  "Xóa thông báo",                       "Notification"),
    };

    public const string ClaimType = "permission";
}

public record PermissionDescriptor(string Key, string Label, string Group);
