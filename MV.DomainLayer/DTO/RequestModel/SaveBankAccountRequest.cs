using System.ComponentModel.DataAnnotations;

namespace MV.DomainLayer.DTO.RequestModel;

public class SaveBankAccountRequest
{
    [Required]
    public string BankName { get; set; } = string.Empty;

    [Required]
    [RegularExpression(@"^\d{8,19}$", ErrorMessage = "Account number must be 8-19 digits")]
    public string AccountNumber { get; set; } = string.Empty;

    [Required]
    [RegularExpression(@"^[A-Z\s]+$", ErrorMessage = "Account holder name must be uppercase letters only")]
    public string AccountHolderName { get; set; } = string.Empty;
}
