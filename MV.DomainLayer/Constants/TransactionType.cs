namespace MV.DomainLayer.Constants;

public static class TransactionType
{
    public const string Deposit = "Deposit";
    public const string Payment = "Payment";
    public const string EscrowCredit = "EscrowCredit";

    /// <summary>Giải ngân escrow vào số dư khả dụng của tutor khi buổi học HOÀN TẤT — đây là thu nhập thật.
    /// Chỉ loại này mới được tính vào totalEarned/earnings.</summary>
    public const string EscrowRelease = "EscrowRelease";

    /// <summary>Hoàn/rút escrow khỏi frozen khi booking bị hủy, tutor từ chối, tutor không phản hồi,
    /// hoặc no-show — tutor KHÔNG thực nhận. KHÔNG được tính vào thu nhập.</summary>
    public const string EscrowReversal = "EscrowReversal";

    public const string Withdrawal = "Withdrawal";
    public const string Refund = "Refund";
    public const string DepositPayment = "DepositPayment";
    public const string RemainingPayment = "RemainingPayment";
    public const string BankVerification = "BankVerification";

    /// <summary>Admin/staff chủ động cộng tiền vào ví một user, không gắn với booking hay
    /// yêu cầu rút tiền nào — xem <see cref="Entities.AdminWalletTransfer"/> để biết ai làm và vì sao.</summary>
    public const string AdminCredit = "AdminCredit";

    /// <summary>
    /// Staff/admin đã chuyển tiền thật về tài khoản ngân hàng sau một yêu cầu rút tiền.
    /// </summary>
    public const string BankTransfer = "BankTransfer";
}
