using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.Constants;

namespace MV.DomainLayer.DTO.ResponseModel.Certificate;

/// <summary>
/// Kết quả validation chứng chỉ
/// </summary>
public class CertificateValidationResult
{
    /// <summary>
    /// Chứng chỉ có pass tất cả validation rules không
    /// </summary>
    public bool IsAutoVerified { get; set; }

    /// <summary>
    /// Trạng thái cuối cùng: "verified", "pending_review", "rejected"
    /// </summary>
    public string FinalStatus { get; set; } = CertificateStatus.PendingReview;

    /// <summary>
    /// Ghi chú cho Admin review (nếu cần)
    /// </summary>
    public string? AdminNote { get; set; }

    /// <summary>
    /// Chi tiết từng bước validation
    /// </summary>
    public ValidationDetails Details { get; set; } = new();

    /// <summary>
    /// Dữ liệu OCR đã extract (để admin đỡ phải gõ lại)
    /// </summary>
    public CertificateOcrData? OcrData { get; set; }
}

/// <summary>
/// Chi tiết kết quả từng bước validation chứng chỉ
/// </summary>
public class ValidationDetails
{
    /// <summary>
    /// Kết quả OCR
    /// </summary>
    public bool OcrSuccess { get; set; }
    public double OcrConfidence { get; set; }

    /// <summary>
    /// Kết quả so sánh tên
    /// </summary>
    public bool NameMatched { get; set; }
    public double NameSimilarity { get; set; }
    public string? ExtractedName { get; set; }
    public string? ExpectedName { get; set; }

    /// <summary>
    /// Kết quả kiểm tra Issuer - AI Verification
    /// </summary>
    public bool IssuerValidByAi { get; set; }
    public string? AiIssuerVerificationReason { get; set; }
    public double AiIssuerConfidence { get; set; }
    public string? AiDetectedOrganizationType { get; set; }

    /// <summary>
    /// Kết quả so sánh Issuer OCR với User Input
    /// </summary>
    public bool IssuerMatchesUserInput { get; set; }
    public string? ExtractedIssuer { get; set; }
    public string? UserInputIssuer { get; set; }
    public double IssuerMatchSimilarity { get; set; }

    /// <summary>
    /// Kết quả tổng hợp Issuer validation (AI + Match)
    /// </summary>
    public bool IssuerValidationPassed { get; set; }

    /// <summary>
    /// Kết quả kiểm tra ngày cấp từ OCR
    /// </summary>
    public string? ExtractedIssueDate { get; set; }
    public DateTime? ExtractedDate { get; set; }
    public int? ExtractedYear { get; set; }

    /// <summary>
    /// Ngày từ OCR có hợp lệ không (parse được và không phải tương lai)
    /// </summary>
    public bool ExtractedDateIsValid { get; set; }

    /// <summary>
    /// So sánh năm OCR với năm user input
    /// </summary>
    public bool YearMatchesUserInput { get; set; }
    public int? UserInputYear { get; set; }

    /// <summary>
    /// Năm user input có hợp lệ không (1950 đến hiện tại)
    /// </summary>
    public bool UserInputYearIsValid { get; set; }

    /// <summary>
    /// Kết quả tổng hợp Date/Year validation (ExtractedDateIsValid + YearMatchesUserInput + UserInputYearIsValid)
    /// </summary>
    public bool DateValidationPassed { get; set; }

    public string? ValidationMessage { get; set; }

    /// <summary>
    /// Danh sách các lỗi validation
    /// </summary>
    public List<string> Errors { get; set; } = new();
}
