using MV.DomainLayer.DTO.ResponseModel;

namespace MV.DomainLayer.DTO;

/// <summary>
/// Tùy chọn cho luồng xác minh CCCD/eKYC dùng chung (tutor & học sinh).
/// </summary>
public class EkycVerificationOptions
{
    /// <summary>
    /// Bắt buộc đọc được CCCD. true → OCR thất bại thì ném lỗi (học sinh cần DOB để chứng minh tuổi).
    /// false → cho phép OCR thất bại, chuyển sang xác minh thủ công (luồng tutor cũ).
    /// </summary>
    public bool RequireOcr { get; set; }

    /// <summary>
    /// Nếu có, DOB trên CCCD phải &gt;= độ tuổi này, nếu không sẽ ném lỗi và KHÔNG đánh dấu đã xác minh.
    /// </summary>
    public int? MinAgeRequired { get; set; }

    /// <summary>Tự động điền các trường cá nhân còn trống (fullname/address/gender/birthdate) từ CCCD.</summary>
    public bool AutoFillProfile { get; set; } = true;
}

/// <summary>
/// Kết quả xác minh CCCD/eKYC dùng chung.
/// </summary>
public class EkycVerificationResult
{
    /// <summary>Kết quả OCR thô (null nếu OCR thất bại và RequireOcr=false).</summary>
    public FptAiResult? Ocr { get; set; }

    /// <summary>Ngày sinh đọc được từ CCCD (nếu parse được).</summary>
    public DateOnly? DateOfBirth { get; set; }

    /// <summary>Đã đánh dấu xác minh danh tính hay chưa.</summary>
    public bool Verified { get; set; }

    /// <summary>Response chuẩn hóa để trả về client (caller có thể chỉnh Message).</summary>
    public CccdUploadResponse Response { get; set; } = new();
}
