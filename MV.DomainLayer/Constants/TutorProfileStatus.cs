namespace MV.DomainLayer.Constants
{
    public static class TutorProfileStatus
    {
        public const string Draft = "draft";                      // Mặc định khi mới tạo, đang điền thông tin
        public const string PendingApproval = "pending_approval"; // Certificate invalid, chờ Admin duyệt
        public const string Active = "active";                    // Đã được duyệt và hiển thị công khai
        public const string Rejected = "rejected";                // Bị Admin từ chối

        /// <summary>
        /// Giá trị TỔNG HỢP, chỉ dùng trong response API (ProfileCompletionResponse.ProfileStatus)
        /// khi profile đang Active nhưng có 1 bản chỉnh sửa đang chờ Admin duyệt trong Redis
        /// (xem ITutorProfileUpdateStagingService). KHÔNG BAO GIỜ được ghi xuống cột
        /// Tutorprofile.Profilestatus — cột đó phải giữ nguyên Active để Marketplace không ẩn tutor.
        /// </summary>
        public const string PendingUpdate = "pending_update";
    }
}
