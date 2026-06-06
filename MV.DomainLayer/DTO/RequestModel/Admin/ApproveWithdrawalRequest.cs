using System.ComponentModel.DataAnnotations;

namespace MV.DomainLayer.DTO.RequestModel.Admin;

public class ApproveWithdrawalRequest
{
    [MaxLength(500)]
    public string? Note { get; set; }
}
