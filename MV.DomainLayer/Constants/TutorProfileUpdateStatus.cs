namespace MV.DomainLayer.Constants
{
    /// <summary>
    /// Trạng thái của 1 bản đề xuất chỉnh sửa hồ sơ do Tutor (đã Active) gửi lên, chờ Admin duyệt.
    /// Lưu trong Redis (xem ITutorProfileUpdateStagingService) — độc lập với Tutorprofile.Profilestatus,
    /// vốn phải giữ nguyên "active" trong suốt quá trình chờ duyệt để Marketplace tiếp tục hiển thị
    /// thông tin cũ.
    /// </summary>
    public static class TutorProfileUpdateStatus
    {
        public const string Pending = "pending";
        public const string Approved = "approved";
        public const string Rejected = "rejected";
    }
}
