namespace MV.DomainLayer.DTO.ResponseModel
{
    public class ReviewProfileUpdateResponse
    {
        public string TutorName { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty; // "approved" | "rejected" — xem TutorProfileUpdateStatus
        public string? Note { get; set; }

        /// <summary>
        /// true nếu Tutor đã nộp thêm 1 thay đổi mới trong lúc Admin đang xử lý request này —
        /// thay đổi mới đó KHÔNG bị mất, vẫn nằm trong hàng chờ và cần Admin duyệt lại riêng.
        /// </summary>
        public bool HasNewerPendingChanges { get; set; }
    }
}
