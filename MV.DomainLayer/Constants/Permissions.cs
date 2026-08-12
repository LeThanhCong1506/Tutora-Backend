namespace MV.DomainLayer.Constants;

public static class Permissions
{
    // Tutor onboarding & certificates
    public const string TutorApprovalView = "tutor_approval.view";
    public const string TutorApprovalDecide = "tutor_approval.decide";
    public const string CertificateView = "certificate.view";
    public const string CertificateVerify = "certificate.verify";
    public const string TutorCccdView = "tutor_cccd.view";
    public const string TutorProfileUpdateView = "tutor_profile_update.view";
    public const string TutorProfileUpdateDecide = "tutor_profile_update.decide";

    // User management (không bao gồm ChangeUserRole/DeleteUser - luôn Admin-only)
    public const string UserView = "user.view";
    public const string UserUpdate = "user.update";
    public const string UserDeactivate = "user.deactivate";

    public const string DashboardView = "dashboard.view";
    public const string FinancialView = "financial.view";
    public const string BookingView = "booking.view";
    public const string PaymentConfirm = "payment.confirm";
    public const string PromotionManage = "promotion.manage";

    public const string PayoutView = "payout.view";
    public const string PayoutApprove = "payout.approve";
    public const string PayoutReject = "payout.reject";
    /// <summary>
    /// Chuyển thẳng tiền từ hệ thống vào ví một user bất kỳ (không gắn với yêu cầu rút tiền
    /// nào) — tách riêng khỏi PayoutApprove vì đây là quyền phát sinh tiền chủ động, rủi ro
    /// cao hơn hẳn việc duyệt một yêu cầu do chính user tạo ra.
    /// </summary>
    public const string PayoutTransfer = "payout.transfer";
    public const string FraudLogView = "fraud_log.view";
    public const string SystemAlertView = "system_alert.view";
    public const string SystemAlertResolve = "system_alert.resolve";

    public const string DisputeView = "dispute.view";
    public const string DisputeInvestigate = "dispute.investigate";
    public const string DisputeResolve = "dispute.resolve";

    public const string WarningCreate = "warning.create";
    public const string WarningView = "warning.view";
    public const string SuspensionManage = "suspension.manage";

    public const string FeedbackView = "feedback.view";
    public const string FeedbackModerate = "feedback.moderate";

    public const string PolicyView = "policy.view";
    public const string PolicyManage = "policy.manage";

    public const string ExportData = "export.data";

    public const string NotificationSend = "notification.send";
    public const string NotificationView = "notification.view";
    public const string NotificationDelete = "notification.delete";

    // CMS lookup management
    public const string LookupView = "lookup.view";
    public const string LookupCreate = "lookup.create";
    public const string LookupUpdate = "lookup.update";
    public const string LookupDelete = "lookup.delete";

    // Question bank & PDF extraction
    public const string QuestionBankView = "question_bank.view";
    public const string QuestionBankCreate = "question_bank.create";
    public const string QuestionBankUpdate = "question_bank.update";
    public const string QuestionBankDelete = "question_bank.delete";
    public const string QuestionDocumentView = "question_document.view";
    public const string QuestionDocumentUpload = "question_document.upload";

    // Knowledge Base (nội dung/chính sách Tutora cho bot FAQ)
    public const string KnowledgeBaseView = "knowledge_base.view";
    public const string KnowledgeBaseUpload = "knowledge_base.upload";
    public const string KnowledgeBaseDelete = "knowledge_base.delete";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        TutorApprovalView, TutorApprovalDecide, CertificateView, CertificateVerify, TutorCccdView,
        TutorProfileUpdateView, TutorProfileUpdateDecide,
        UserView, UserUpdate, UserDeactivate,
        DashboardView, FinancialView, BookingView, PaymentConfirm, PromotionManage,
        PayoutView, PayoutApprove, PayoutReject, PayoutTransfer, FraudLogView,
        SystemAlertView, SystemAlertResolve,
        DisputeView, DisputeInvestigate, DisputeResolve,
        WarningCreate, WarningView, SuspensionManage,
        FeedbackView, FeedbackModerate,
        PolicyView, PolicyManage,
        ExportData,
        NotificationSend, NotificationView, NotificationDelete,
        LookupView, LookupCreate, LookupUpdate, LookupDelete,
        QuestionBankView, QuestionBankCreate, QuestionBankUpdate, QuestionBankDelete,
        QuestionDocumentView, QuestionDocumentUpload,
        KnowledgeBaseView, KnowledgeBaseUpload, KnowledgeBaseDelete
    };

    /// <summary>
    /// Single source of truth for authorization and the CMS permission-group editor.
    /// Domain/module/action are presentation metadata; Requires contains prerequisite keys.
    /// </summary>
    public static readonly IReadOnlyList<PermissionDescriptor> Catalog = new List<PermissionDescriptor>
    {
        new(TutorApprovalView, "Xem hồ sơ gia sư chờ duyệt", "Chuyên môn", "Duyệt gia sư", "Xem"),
        new(TutorApprovalDecide, "Duyệt hoặc từ chối hồ sơ gia sư", "Chuyên môn", "Duyệt gia sư", "Duyệt", TutorApprovalView),
        new(CertificateView, "Xem chứng chỉ chờ duyệt", "Chuyên môn", "Chứng chỉ", "Xem"),
        new(CertificateVerify, "Xác minh chứng chỉ gia sư", "Chuyên môn", "Chứng chỉ", "Duyệt", CertificateView),
        new(TutorCccdView, "Xem ảnh CCCD gia sư", "Chuyên môn", "Định danh gia sư", "Xem"),
        new(TutorProfileUpdateView, "Xem yêu cầu cập nhật hồ sơ gia sư", "Chuyên môn", "Cập nhật hồ sơ gia sư", "Xem"),
        new(TutorProfileUpdateDecide, "Duyệt hoặc từ chối cập nhật hồ sơ gia sư", "Chuyên môn", "Cập nhật hồ sơ gia sư", "Duyệt", TutorProfileUpdateView),

        new(UserView, "Xem danh sách và chi tiết người dùng", "CMS / Nghiệp vụ", "Người dùng", "Xem"),
        new(UserUpdate, "Chỉnh sửa thông tin người dùng", "CMS / Nghiệp vụ", "Người dùng", "Sửa", UserView),
        new(UserDeactivate, "Khóa hoặc mở khóa tài khoản", "CMS / Nghiệp vụ", "Người dùng", "Khóa", UserView),
        new(BookingView, "Xem danh sách và chi tiết booking", "CMS / Nghiệp vụ", "Booking", "Xem"),
        new(PromotionManage, "Quản lý mã khuyến mãi", "CMS / Nghiệp vụ", "Khuyến mãi", "Quản lý"),

        new(LookupView, "Xem danh mục học tập", "Danh mục", "Danh mục học tập", "Xem"),
        new(LookupCreate, "Thêm danh mục học tập", "Danh mục", "Danh mục học tập", "Thêm", LookupView),
        new(LookupUpdate, "Sửa danh mục học tập", "Danh mục", "Danh mục học tập", "Sửa", LookupView),
        new(LookupDelete, "Xóa hoặc ngừng dùng danh mục học tập", "Danh mục", "Danh mục học tập", "Xóa", LookupView),

        new(QuestionBankView, "Xem ngân hàng câu hỏi", "Nội dung", "Ngân hàng câu hỏi", "Xem"),
        new(QuestionBankCreate, "Thêm câu hỏi", "Nội dung", "Ngân hàng câu hỏi", "Thêm", QuestionBankView),
        new(QuestionBankUpdate, "Sửa câu hỏi", "Nội dung", "Ngân hàng câu hỏi", "Sửa", QuestionBankView),
        new(QuestionBankDelete, "Xóa câu hỏi", "Nội dung", "Ngân hàng câu hỏi", "Xóa", QuestionBankView),
        new(QuestionDocumentView, "Xem lịch sử trích xuất PDF", "Nội dung", "Trích xuất câu hỏi từ PDF", "Xem"),
        new(QuestionDocumentUpload, "Upload PDF và trích xuất câu hỏi", "Nội dung", "Trích xuất câu hỏi từ PDF", "Upload", QuestionDocumentView, QuestionBankCreate),
        new(KnowledgeBaseView, "Xem tài liệu về Tutora", "Nội dung", "Knowledge Base", "Xem"),
        new(KnowledgeBaseUpload, "Tải lên tài liệu về Tutora", "Nội dung", "Knowledge Base", "Upload", KnowledgeBaseView),
        new(KnowledgeBaseDelete, "Xóa tài liệu về Tutora", "Nội dung", "Knowledge Base", "Xóa", KnowledgeBaseView),

        new(DashboardView, "Xem dashboard và báo cáo thống kê", "Báo cáo", "Tổng quan", "Xem"),
        new(FinancialView, "Xem báo cáo tài chính", "Báo cáo", "Tài chính", "Xem"),
        new(ExportData, "Xuất dữ liệu", "Báo cáo", "Xuất dữ liệu", "Xuất"),

        new(PaymentConfirm, "Xác nhận thanh toán thủ công", "Tài chính", "Thanh toán", "Xác nhận"),

        new(PayoutView, "Xem yêu cầu rút tiền", "Tài chính", "Rút tiền", "Xem"),
        new(PayoutApprove, "Duyệt yêu cầu rút tiền", "Tài chính", "Rút tiền", "Duyệt", PayoutView),
        new(PayoutReject, "Từ chối yêu cầu rút tiền", "Tài chính", "Rút tiền", "Từ chối", PayoutView),
        new(PayoutTransfer, "Chuyển tiền chủ động cho user", "Tài chính", "Rút tiền", "Chuyển tiền", PayoutView),
        new(FraudLogView, "Xem nhật ký gian lận (lịch sử)", "Tài chính", "Nhật ký gian lận", "Xem"),
        new(SystemAlertView, "Xem cảnh báo hệ thống", "Tài chính", "Cảnh báo hệ thống", "Xem"),
        new(SystemAlertResolve, "Xử lý cảnh báo hệ thống", "Tài chính", "Cảnh báo hệ thống", "Xử lý", SystemAlertView),

        new(DisputeView, "Xem tranh chấp", "CMS / Nghiệp vụ", "Tranh chấp", "Xem"),
        new(DisputeInvestigate, "Điều tra tranh chấp", "CMS / Nghiệp vụ", "Tranh chấp", "Điều tra", DisputeView),
        new(DisputeResolve, "Giải quyết tranh chấp", "CMS / Nghiệp vụ", "Tranh chấp", "Giải quyết", DisputeView),
        new(WarningView, "Xem lịch sử cảnh cáo và đình chỉ", "CMS / Nghiệp vụ", "Cảnh cáo & đình chỉ", "Xem"),
        new(WarningCreate, "Tạo cảnh cáo người dùng", "CMS / Nghiệp vụ", "Cảnh cáo & đình chỉ", "Thêm", WarningView),
        new(SuspensionManage, "Đình chỉ hoặc gỡ đình chỉ tài khoản", "CMS / Nghiệp vụ", "Cảnh cáo & đình chỉ", "Quản lý", WarningView),
        new(FeedbackView, "Xem đánh giá gia sư", "CMS / Nghiệp vụ", "Đánh giá", "Xem"),
        new(FeedbackModerate, "Ẩn hoặc hiện đánh giá", "CMS / Nghiệp vụ", "Đánh giá", "Kiểm duyệt", FeedbackView),
        new(PolicyView, "Xem văn bản chính sách", "CMS / Nội dung", "Chính sách", "Xem"),
        new(PolicyManage, "Tạo, sửa, xuất bản văn bản chính sách", "CMS / Nội dung", "Chính sách", "Quản lý", PolicyView),

        new(NotificationView, "Xem thông báo hệ thống", "Khác", "Thông báo", "Xem"),
        new(NotificationSend, "Gửi thông báo", "Khác", "Thông báo", "Gửi", NotificationView),
        new(NotificationDelete, "Xóa thông báo", "Khác", "Thông báo", "Xóa", NotificationView),
    };

    public const string ClaimType = "permission";
}

public sealed record PermissionDescriptor
{
    public PermissionDescriptor(
        string key,
        string label,
        string domain,
        string module,
        string action,
        params string[] requires)
    {
        Key = key;
        Label = label;
        Domain = domain;
        Module = module;
        Action = action;
        Requires = requires;
    }

    public string Key { get; }
    public string Label { get; }
    public string Domain { get; }
    public string Module { get; }
    public string Action { get; }
    public IReadOnlyList<string> Requires { get; }

    // Temporary compatibility for clients that grouped the old catalog by Group.
    public string Group => Module;
}
