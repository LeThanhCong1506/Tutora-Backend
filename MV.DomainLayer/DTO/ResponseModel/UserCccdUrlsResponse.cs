namespace MV.DomainLayer.DTO.ResponseModel
{
    /// <summary>Dùng chung cho Tutor lẫn Student (cả 2 role đều là row trong bảng users, cùng cột CCCD).</summary>
    public class UserCccdUrlsResponse
    {
        public string UserId { get; set; } = string.Empty;
        public string? UserFullName { get; set; }
        /// <summary>Signed URL, hết hạn sau ~15 phút — null nếu người dùng chưa từng upload/verify CCCD.</summary>
        public string? FrontImageUrl { get; set; }
        public string? BackImageUrl { get; set; }
        public bool IsIdentityVerified { get; set; }
        /// <summary>true khi OCR tự động thất bại đủ ngưỡng lần liên tiếp — ảnh đã nhận nhưng đang chờ
        /// Admin xem thủ công. Luôn false khi IsIdentityVerified = true.</summary>
        public bool IsPendingReview { get; set; }
    }
}
