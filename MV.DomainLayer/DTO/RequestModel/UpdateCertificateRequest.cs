using Microsoft.AspNetCore.Http;
using System.ComponentModel.DataAnnotations;

namespace MV.DomainLayer.DTO.RequestModel
{
    public class UpdateCertificateRequest
    {
        /// <summary>
        /// Ten chung chi / bang cap (VD: TOEFL iBT 115, IELTS 8.0, Bang Cu nhan...)
        /// </summary>
        [Required(ErrorMessage = "Certificate name is required")]
        [MaxLength(200, ErrorMessage = "Certificate name must not exceed 200 characters")]
        public string CertificateName { get; set; } = string.Empty;

        /// <summary>
        /// Loai chung chi: degree (bang cap), language (ngoai ngu), professional (chuyen mon)
        /// </summary>
        [Required(ErrorMessage = "Certificate type is required")]
        [MaxLength(50, ErrorMessage = "Certificate type must not exceed 50 characters")]
        public string CertificateType { get; set; } = string.Empty;

        /// <summary>
        /// To chuc cap (VD: ETS Global, British Council, Dai hoc Bach Khoa...)
        /// </summary>
        [Required(ErrorMessage = "Issuing organization is required")]
        [MaxLength(200, ErrorMessage = "Issuing organization must not exceed 200 characters")]
        public string IssuingOrganization { get; set; } = string.Empty;

        /// <summary>
        /// Nam cap (optional)
        /// </summary>
        public int? YearIssued { get; set; }

        /// <summary>
        /// Ma chung chi de verify (optional)
        /// </summary>
        [MaxLength(100, ErrorMessage = "Credential ID must not exceed 100 characters")]
        public string? CredentialId { get; set; }

        /// <summary>
        /// Link xac thuc chung chi online (optional)
        /// </summary>
        [MaxLength(500, ErrorMessage = "Credential URL must not exceed 500 characters")]
        public string? CredentialUrl { get; set; }

        /// <summary>
        /// File chung chi upload moi (optional - chi can khi muon thay file cu)
        /// </summary>
        public IFormFile? CertificateFile { get; set; }
    }
}
