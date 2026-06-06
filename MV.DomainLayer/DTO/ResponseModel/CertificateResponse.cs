namespace MV.DomainLayer.DTO.ResponseModel
{
    public class CertificateResponse
    {
        public string CertificateId { get; set; } = string.Empty;
        public string CertificateName { get; set; } = string.Empty;
        public string CertificateType { get; set; } = string.Empty;
        public string IssuingOrganization { get; set; } = string.Empty;
        public int? YearIssued { get; set; }
        public string? CredentialId { get; set; }
        public string? CredentialUrl { get; set; }
        public string CertificateFileUrl { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        
        /// <summary>
        /// Certificate verification status: "verified", "pending_review", "rejected"
        /// </summary>
        public string? VerificationStatus { get; set; }
        
        /// <summary>
        /// Admin note or auto-check result details
        /// </summary>
        public string? VerificationNote { get; set; }
    }

    /// <summary>
    /// Response khi upload certificate - bao gồm kết quả auto-check
    /// </summary>
    public class CertificateUploadResponse
    {
        public CertificateResponse Certificate { get; set; } = null!;
        public CertificateValidationSummary ValidationResult { get; set; } = null!;
        
        /// <summary>
        /// Nếu true → Profile đã được set Active
        /// Nếu false → FE cần hiện popup để user chọn upload lại hoặc gửi Admin
        /// </summary>
        public bool IsProfileActivated { get; set; }
    }

    /// <summary>
    /// Kết quả auto-check certificate (trả về cho FE)
    /// </summary>
    public class CertificateValidationSummary
    {
        public bool IsValid { get; set; }
        public List<string> Errors { get; set; } = new();
        public ValidationCheckResults Checks { get; set; } = new();
    }

    public class ValidationCheckResults
    {
        public bool NameMatched { get; set; }
        public double NameSimilarity { get; set; }
        
        /// <summary>
        /// AI đã xác nhận issuer là tổ chức hợp lệ
        /// </summary>
        public bool IssuerValidByAi { get; set; }
        
        /// <summary>
        /// Issuer từ OCR khớp với issuer user nhập
        /// </summary>
        public bool IssuerMatchesUserInput { get; set; }
        
        /// <summary>
        /// Kết quả tổng hợp Issuer: cả AI verify và matching đều pass
        /// </summary>
        public bool IssuerValidationPassed { get; set; }
        
        /// <summary>
        /// Ngày từ OCR có hợp lệ không (parse được và không phải tương lai)
        /// </summary>
        public bool ExtractedDateIsValid { get; set; }
        
        /// <summary>
        /// Năm từ OCR khớp với năm user nhập
        /// </summary>
        public bool YearMatchesUserInput { get; set; }
        
        /// <summary>
        /// Năm user input có hợp lệ không (1950 đến hiện tại)
        /// </summary>
        public bool UserInputYearIsValid { get; set; }
        
        /// <summary>
        /// Kết quả tổng hợp Date: tất cả các check đều pass
        /// </summary>
        public bool DateValidationPassed { get; set; }
        
        public bool OcrSuccess { get; set; }
        public double OcrConfidence { get; set; }
    }
}



