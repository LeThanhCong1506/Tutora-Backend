using System.ComponentModel.DataAnnotations;

namespace MV.DomainLayer.DTO.RequestModel.Admin;

public class RejectWithdrawalRequest
{
    [Required(ErrorMessage = "Reason is required when rejecting a withdrawal")]
    [MaxLength(500)]
    public string Reason { get; set; } = string.Empty;
}
