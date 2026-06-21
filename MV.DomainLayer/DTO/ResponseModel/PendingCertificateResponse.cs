namespace MV.DomainLayer.DTO.ResponseModel
{
    public class PendingCertificateResponse
    {
        // ── Certificate info ──────────────────────────────────────────────
        public string CertificateId { get; set; } = string.Empty;
        public string CertificateName { get; set; } = string.Empty;
        public string IssuingOrganization { get; set; } = string.Empty;
        public int? YearIssued { get; set; }
        public string CertificateFileUrl { get; set; } = string.Empty;
        public string? VerificationStatus { get; set; }
        public string? VerificationNote { get; set; }
        public DateTime? CreatedAt { get; set; }

        // ── Tutor info ────────────────────────────────────────────────────
        public string TutorId { get; set; } = string.Empty;
        public string? TutorFullName { get; set; }
        public string? TutorEmail { get; set; }
        public string? TutorAvatarUrl { get; set; }
    }
}
