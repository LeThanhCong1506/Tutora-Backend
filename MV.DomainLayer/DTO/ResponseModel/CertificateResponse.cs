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

        /// <summary>Trạng thái duyệt: "pending_review" | "verified" | "rejected"</summary>
        public string? VerificationStatus { get; set; }

        /// <summary>Ghi chú của Admin khi duyệt hoặc từ chối</summary>
        public string? VerificationNote { get; set; }
    }

    /// <summary>
    /// Response khi upload chứng chỉ. Chứng chỉ luôn ở trạng thái PendingReview —
    /// Admin/Staff sẽ xét duyệt thủ công.
    /// </summary>
    public class CertificateUploadResponse
    {
        public CertificateResponse Certificate { get; set; } = null!;
    }
}



