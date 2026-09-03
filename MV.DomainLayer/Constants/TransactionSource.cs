namespace MV.DomainLayer.Constants;

/// <summary>
/// Bảng gốc của một dòng trong lịch sử giao dịch.
/// </summary>
public static class TransactionSource
{
    /// <summary>wallet_transactions — mọi biến động số dư ví.</summary>
    public const string Wallet = "Wallet";

    /// <summary>payment_transactions — lệnh chi/thu đi qua ngân hàng hoặc cổng thanh toán.</summary>
    public const string Payment = "Payment";
}

/// <summary>
/// Hình thức tiền di chuyển
/// </summary>
public static class TransactionChannel
{
    public const string Wallet = "Wallet";
    public const string Bank = "Bank";
}
