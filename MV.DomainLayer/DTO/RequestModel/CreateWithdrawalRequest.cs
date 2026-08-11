using System.ComponentModel.DataAnnotations;

namespace MV.DomainLayer.DTO.RequestModel;

/// <summary>
/// Bank destination always comes from the requester's saved BankAccount now (Tutor and
/// Parent/Student alike) — see BankAccountController/IBankAccountService. Typing bank details
/// per-withdrawal was removed so that changing a payout destination always goes through the
/// OTP-gated bank-account save flow.
/// </summary>
public class CreateWithdrawalRequest
{
    [Required]
    [Range(10000, double.MaxValue, ErrorMessage = "Số tiền rút tối thiểu là 10.000 VND")]
    public decimal Amount { get; set; }
}
