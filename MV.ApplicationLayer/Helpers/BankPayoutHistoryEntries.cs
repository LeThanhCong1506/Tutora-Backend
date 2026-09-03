using Microsoft.EntityFrameworkCore;
using MV.ApplicationLayer.Interfaces;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.ResponseModel;

namespace MV.ApplicationLayer.Helpers;

/// <summary>
/// Dựng các dòng "Chuyển tiền ngân hàng" cho lịch sử giao dịch.
/// </summary>
public static class BankPayoutHistoryEntries
{
    /// <summary>
    /// Che số tài khoản, chỉ giữ 4 số cuối
    /// </summary>
    public static string? MaskAccountNumber(string? accountNumber)
    {
        if (string.IsNullOrWhiteSpace(accountNumber))
            return null;

        var trimmed = accountNumber.Trim();
        return trimmed.Length <= 4 ? trimmed : $"****{trimmed[^4..]}";
    }

    /// <summary>
    /// Query gốc: mọi lệnh chi thủ công đã thành công của <paramref name="userId"/>.
    /// </summary>
    public static IQueryable<PayoutRow> Query(IAppDbContext context, string userId) =>
        context.PaymentTransactions
            .AsNoTracking()
            .Where(t => t.Userid == userId
                        && t.Purpose == PaymentTransactionPurpose.Withdrawal
                        && t.Status == PaymentTransactionStatus.Succeeded)
            .Select(t => new PayoutRow
            {
                PaymentTransactionId = t.Paymenttransactionid,
                Amount = t.Amount,
                WithdrawalId = t.Withdrawalid,
                ProviderTransactionId = t.Providertransactionid,
                BankTransactionCode = t.Banktransactioncode,
                BankName = t.Destinationaccountbankname,
                AccountNumber = t.Destinationaccountnumber,
                ProofImagePath = t.Proofimagepath,
                // Mốc thời gian của dòng này là lúc tiền thật sự rời ngân hàng
                PaidAt = t.Paidat,
                CreatedAt = t.Createdat
            });

    /// <summary>Chiếu <see cref="PayoutRow"/> sang dòng lịch sử giao dịch.</summary>
    public static TransactionHistoryResponse ToHistoryEntry(
        PayoutRow row, IFileStorageService fileStorageService)
    {
        var occurredAt = row.PaidAt ?? row.CreatedAt ?? MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;

        return new TransactionHistoryResponse
        {
            TransactionId = row.PaymentTransactionId,
            // Dương: đứng ở phía người nhận, tiền đã VÀO tài khoản ngân hàng.
            Amount = Math.Abs(row.Amount),
            TransactionType = TransactionType.BankTransfer,
            Description = row.WithdrawalId.HasValue
                ? $"Chuyển tiền về tài khoản ngân hàng cho yêu cầu rút #{row.WithdrawalId}"
                : "Chuyển tiền về tài khoản ngân hàng",
            ReferenceId = row.WithdrawalId,
            ReferenceTable = ReferenceTable.Withdrawal,
            CreatedAt = occurredAt,
            PaidAt = row.PaidAt,
            ProviderTransactionId = row.ProviderTransactionId,
            BankTransactionCode = row.BankTransactionCode,
            BankName = row.BankName,
            AccountNumber = MaskAccountNumber(row.AccountNumber),
            ProofImageUrl = string.IsNullOrWhiteSpace(row.ProofImagePath)
                ? null
                : fileStorageService.GenerateSignedUrl(row.ProofImagePath),
            Source = TransactionSource.Payment,
            Channel = TransactionChannel.Bank,
            IsInformational = true
        };
    }

    /// <summary>Hình chiếu phẳng của payment_transactions, đủ dựng một dòng lịch sử.</summary>
    public class PayoutRow
    {
        public int PaymentTransactionId { get; set; }
        public decimal Amount { get; set; }
        public int? WithdrawalId { get; set; }
        public string? ProviderTransactionId { get; set; }
        public string? BankTransactionCode { get; set; }
        public string? BankName { get; set; }
        public string? AccountNumber { get; set; }
        public string? ProofImagePath { get; set; }
        public DateTime? PaidAt { get; set; }
        public DateTime? CreatedAt { get; set; }
    }
}
