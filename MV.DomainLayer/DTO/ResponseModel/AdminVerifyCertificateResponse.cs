namespace MV.DomainLayer.DTO.ResponseModel
{
    public class AdminVerifyCertificateResponse
    {
        public string CertificateId { get; set; } = string.Empty;
        public string TutorId { get; set; } = string.Empty;
        public string CertificateName { get; set; } = string.Empty;
        public string VerificationStatus { get; set; } = string.Empty;
        public string? VerificationNote { get; set; }
    }
}
