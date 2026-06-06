using System.ComponentModel.DataAnnotations;

namespace MV.DomainLayer.DTO.RequestModel;

public class CreateWithdrawalRequest
{
    [Required]
    [Range(100000, double.MaxValue, ErrorMessage = "Số tiền rút tối thiểu là 100,000 VND")]
    public decimal Amount { get; set; }
}
