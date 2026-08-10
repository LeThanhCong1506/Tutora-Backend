namespace MV.DomainLayer.Constants
{
    /// <summary>
    /// Kết quả của một lần cập nhật mục hồ sơ gia sư (Thông tin cơ bản / Giới thiệu / Môn học & giá).
    /// Khi hồ sơ đã <see cref="TutorProfileStatus.Active"/>, thay đổi không được ghi thẳng vào DB mà
    /// chỉ lưu tạm chờ Admin duyệt (xem RequiresApprovalForEdits trong TutorService) — controller cần
    /// phân biệt 2 trường hợp này để trả đúng thông báo cho FE, tránh báo "đã lưu" trong khi thực ra
    /// đang chờ duyệt.
    /// </summary>
    public enum ProfileUpdateOutcome
    {
        NotFound,
        Applied,
        PendingApproval
    }
}
