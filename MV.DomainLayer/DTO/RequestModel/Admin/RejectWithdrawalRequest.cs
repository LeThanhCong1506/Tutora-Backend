using System.ComponentModel.DataAnnotations;

namespace MV.DomainLayer.DTO.RequestModel.Admin;

public class RejectWithdrawalRequest
{
    [Required(ErrorMessage = "Reason is required when rejecting a withdrawal")]
    [MinLength(3, ErrorMessage = "Reason must contain at least 3 characters")]
    [MaxLength(500)]
    public string Reason { get; set; } = string.Empty;
}
