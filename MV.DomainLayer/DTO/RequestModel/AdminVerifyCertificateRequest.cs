using System.ComponentModel.DataAnnotations;

namespace MV.DomainLayer.DTO.RequestModel
{
    public class AdminVerifyCertificateRequest
    {
        [Required]
        public bool IsApproved { get; set; }

        [StringLength(500, ErrorMessage = "Note cannot exceed 500 characters.")]
        public string? Note { get; set; }
    }
}
