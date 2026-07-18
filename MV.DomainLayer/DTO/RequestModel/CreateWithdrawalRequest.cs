using System.ComponentModel.DataAnnotations;

namespace MV.DomainLayer.DTO.RequestModel;

public class CreateWithdrawalRequest
{
    [Required]
    [Range(10000, double.MaxValue, ErrorMessage = "Số tiền rút tối thiểu là 10.000 VND")]
    public decimal Amount { get; set; }

    [MaxLength(100)]
    public string? BankName { get; set; }

    [RegularExpression(@"^\d{8,19}$", ErrorMessage = "Account number must be 8-19 digits")]
    public string? AccountNumber { get; set; }

    [RegularExpression(@"^[A-Z\s]+$", ErrorMessage = "Account holder name must be uppercase letters only")]
    [MaxLength(100)]
    public string? AccountHolderName { get; set; }
}
