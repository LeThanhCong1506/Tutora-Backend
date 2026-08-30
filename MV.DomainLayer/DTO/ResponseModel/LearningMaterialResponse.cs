namespace MV.DomainLayer.DTO.ResponseModel;

public class LearningMaterialResponse
{
    public int MaterialId { get; set; }
    public string? StudentId { get; set; }
    public int? BookingId { get; set; }
    public string? UploadedBy { get; set; }
    public string OwnerType { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? FileType { get; set; }
    public string FileUrl { get; set; } = string.Empty;
    public int? FileSize { get; set; }
    public bool? IsPublic { get; set; }
    public DateTime? CreatedAt { get; set; }

    /// <summary>
    /// Trạng thái trích nội dung để AI sinh bài tập: processing | ready | failed.
    /// Null = định dạng không trích được (không dùng sinh câu hỏi, vẫn xem/tải bình thường).
    /// FE dựa vào đây để disable tài liệu chưa sẵn sàng trong bộ chọn.
    /// </summary>
    public string? ContentStatus { get; set; }

    public int? PageCount { get; set; }
}
