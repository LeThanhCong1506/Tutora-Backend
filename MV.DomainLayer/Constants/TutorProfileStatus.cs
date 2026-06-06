namespace MV.DomainLayer.Constants
{
    public static class TutorProfileStatus
    {
        public const string Draft = "draft";                      // Mặc định khi mới tạo, đang điền thông tin
        public const string PendingApproval = "pending_approval"; // Certificate invalid, chờ Admin duyệt
        public const string Active = "active";                    // Đã được duyệt và hiển thị công khai
        public const string Rejected = "rejected";                // Bị Admin từ chối
    }
}
