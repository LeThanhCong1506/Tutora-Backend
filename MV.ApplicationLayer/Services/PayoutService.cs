using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.Payout;
using MV.DomainLayer.Entities;
using MV.DomainLayer.Helpers;
using MV.DomainLayer.Interfaces;
using MV.ApplicationLayer.Interfaces;
using System.Text.Json;

namespace MV.ApplicationLayer.Services;

public class PayoutService(
    IPayOSTransferClient payOSClient,
    IAppDbContext context,
    INotificationService notificationService,
    IVietQRClient vietQRClient,
    IOptions<MV.DomainLayer.Settings.PayoutSettings> settings,
    ILogger<PayoutService> logger,
    [FromKeyedServices(ServiceKeys.PayOS.Payout)] PayOS.PayOSClient payOSClientDirect) : IPayoutService
{
    private readonly MV.DomainLayer.Settings.PayoutSettings _settings = settings.Value;
    private readonly PayOS.PayOSClient _payOSClient = payOSClientDirect;

    public async Task<PayoutResult> CreatePayoutAsync(int withdrawalId, CancellationToken ct = default)
    {
        var withdrawal = await context.Withdrawalrequests
            .Include(w => w.User)
            .FirstOrDefaultAsync(w => w.Withdrawalid == withdrawalId, ct);

        if (withdrawal is null)
        {
            logger.LogError("Withdrawal request not found: {WithdrawalId}", withdrawalId);
            return new PayoutResult
            {
                IsSuccess = false,
                ErrorCode = PayoutConstants.ErrorCodes.Unknown,
                ErrorMessage = "Không tìm thấy yêu cầu rút tiền"
            };
        }

        if (!string.IsNullOrEmpty(withdrawal.Payostransactionid))
        {
            logger.LogWarning("Withdrawal already has PayOS transaction: {WithdrawalId}, {PayOSId}",
                withdrawalId, withdrawal.Payostransactionid);
            return new PayoutResult
            {
                IsSuccess = false,
                ErrorCode = PayoutConstants.ErrorCodes.DuplicateOrder,
                ErrorMessage = PayoutConstants.ErrorMessages.DuplicateOrder,
                PayosTransactionId = withdrawal.Payostransactionid
            };
        }

        try
        {
            var tutorProfile = await context.Tutorprofiles
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Tutorid == withdrawal.Userid, ct);

            if (tutorProfile is null || string.IsNullOrEmpty(tutorProfile.Bankname) || string.IsNullOrEmpty(tutorProfile.Bankaccountnumber))
            {
                logger.LogError("Tutor bank info not found or incomplete: {UserId}", withdrawal.Userid);
                await HandlePayoutErrorAsync(withdrawal, PayoutConstants.ErrorCodes.InvalidBankAccount,
                    "Thông tin ngân hàng không đầy đủ hoặc không tìm thấy.", ct);
                return new PayoutResult
                {
                    IsSuccess = false,
                    ErrorCode = PayoutConstants.ErrorCodes.InvalidBankAccount,
                    ErrorMessage = PayoutConstants.ErrorMessages.InvalidBankAccount,
                    ShouldRetry = false
                };
            }

            var banks = await vietQRClient.GetBankListAsync(ct);
            var bank = banks.FirstOrDefault(b => b.Code == tutorProfile.Bankname);

            if (bank?.Bin is null)
            {
                logger.LogError("Bank BIN not found for bank code: {BankCode}", tutorProfile.Bankname);
                await HandlePayoutErrorAsync(withdrawal, PayoutConstants.ErrorCodes.InvalidBankAccount,
                    "Mã ngân hàng không hợp lệ. Vui lòng cập nhật thông tin ngân hàng.", ct);
                return new PayoutResult
                {
                    IsSuccess = false,
                    ErrorCode = PayoutConstants.ErrorCodes.InvalidBankAccount,
                    ErrorMessage = "Mã ngân hàng không hợp lệ. Vui lòng cập nhật thông tin ngân hàng.",
                    ShouldRetry = false
                };
            }

            var transferRequest = new TransferRequest
            {
                Amount = withdrawal.Amount ?? 0,
                ToBin = bank.Bin,
                ToAccountNumber = tutorProfile.Bankaccountnumber,
                Description = $"Withdrawal {withdrawalId}",
                Reference = $"WD-{withdrawalId}"
            };

            logger.LogInformation("Creating payout for withdrawal {WithdrawalId}, Amount={Amount}",
                withdrawalId, withdrawal.Amount);

            var response = await payOSClient.TransferAsync(transferRequest, ct);

            if (response.IsSuccess)
            {
                withdrawal.Payostransactionid = response.TransactionId;
                withdrawal.Payosstatus = PayoutConstants.PayOSStatus.Success;
                withdrawal.Status = WithdrawalStatus.Approved;
                withdrawal.Processedat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;

                await context.SaveChangesAsync(ct);

                logger.LogInformation("Payout successful: Withdrawal={WithdrawalId}, PayOSId={PayOSId}",
                    withdrawalId, response.TransactionId);

                await notificationService.CreateNotificationAsync(new MV.DomainLayer.DTO.RequestModel.NotificationRequest
                {
                    Userid = withdrawal.Userid!,
                    Title = "Rút tiền thành công",
                    Message = $"Yêu cầu rút tiền {withdrawal.Amount:N0} VND của bạn đã được xử lý thành công."
                });

                return new PayoutResult
                {
                    IsSuccess = true,
                    PayosTransactionId = response.TransactionId
                };
            }

            // DuplicateOrder with our own stable reference (WD-{id}) means the transfer was already
            // accepted by PayOS in a previous attempt but we crashed before saving txid.
            // Keep Status=Pending — do NOT reject or refund — and alert admin to reconcile manually.
            if (response.ErrorCode == PayoutConstants.ErrorCodes.DuplicateOrder)
            {
                logger.LogWarning(
                    "DuplicateOrder for withdrawal {WithdrawalId} with stable reference WD-{WithdrawalId} — " +
                    "transfer likely in-flight from prior attempt. Keeping Pending, alerting admin.",
                    withdrawalId, withdrawalId);
                withdrawal.Payosresponsecode = response.ErrorCode;
                withdrawal.Payosstatus = PayoutConstants.PayOSStatus.Processing;
                withdrawal.Payoserror = "Transfer may already be in flight; admin must look up PayOS dashboard and record the txid.";
                await context.SaveChangesAsync(ct);
                await CreateStuckPayoutAlertAsync(withdrawalId, withdrawal.Amount ?? 0, ct);
                return new PayoutResult
                {
                    IsSuccess = false,
                    ErrorCode = PayoutConstants.ErrorCodes.DuplicateOrder,
                    ErrorMessage = "Transfer may already be in flight",
                    ShouldRetry = false
                };
            }

            await HandlePayoutErrorAsync(withdrawal, response.ErrorCode ?? PayoutConstants.ErrorCodes.Unknown,
                response.ErrorMessage ?? PayoutConstants.ErrorMessages.Unknown, ct);

            var shouldRetry = ShouldRetryError(response.ErrorCode);
            var retryDelay = CalculateRetryDelay(withdrawal.Retrycount ?? 0, response.ErrorCode);

            if (response.ErrorCode == PayoutConstants.ErrorCodes.InsufficientBalance)
                await CreateInsufficientBalanceAlertAsync(withdrawalId, withdrawal.Amount ?? 0, ct);

            return new PayoutResult
            {
                IsSuccess = false,
                ErrorCode = response.ErrorCode,
                ErrorMessage = response.ErrorMessage,
                ShouldRetry = shouldRetry,
                RetryDelayMinutes = retryDelay
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error creating payout for withdrawal {WithdrawalId}", withdrawalId);
            await HandlePayoutErrorAsync(withdrawal, PayoutConstants.ErrorCodes.Unknown, ex.Message, ct);

            return new PayoutResult
            {
                IsSuccess = false,
                ErrorCode = PayoutConstants.ErrorCodes.Unknown,
                ErrorMessage = PayoutConstants.ErrorMessages.Unknown,
                ShouldRetry = true,
                RetryDelayMinutes = _settings.RetryDelayMinutes
            };
        }
    }

    public async Task<PayoutStatusResult> GetPayoutStatusAsync(string payoutId, CancellationToken ct = default)
    {
        try
        {
            var response = await payOSClient.GetPayoutStatusAsync(payoutId, ct);

            return !response.IsSuccess
                ? new PayoutStatusResult
                {
                    Status = PayoutConstants.Status.Unknown,
                    ErrorCode = response.ErrorCode,
                    ErrorMessage = response.ErrorMessage
                }
                : new PayoutStatusResult
                {
                    Status = response.Status,
                    TransactionId = response.TransactionId,
                    Amount = response.Amount
                };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting payout status: {PayoutId}", payoutId);
            return new PayoutStatusResult
            {
                Status = PayoutConstants.Status.Error,
                ErrorCode = PayoutConstants.ErrorCodes.Unknown,
                ErrorMessage = ex.Message
            };
        }
    }

    public async Task ProcessPendingPayoutsAsync(CancellationToken ct = default)
    {
        try
        {
            logger.LogInformation("Starting pending payouts processing");

            var now = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;
            var pendingWithdrawals = await context.Withdrawalrequests
                .Where(w => w.Status == WithdrawalStatus.Pending
                    && string.IsNullOrEmpty(w.Payostransactionid)
                    && (w.Retrycount == null || w.Retrycount < _settings.MaxRetries)
                    && (w.Lastretryat == null || w.Lastretryat <= now.AddMinutes(-_settings.RetryDelayMinutes)))
                .OrderBy(w => w.Requestedat)
                .Take(_settings.MaxConcurrentPayouts)
                .ToListAsync(ct);

            if (pendingWithdrawals.Count == 0)
            {
                logger.LogInformation("No pending payouts to process");
                return;
            }

            logger.LogInformation("Processing {Count} pending payouts", pendingWithdrawals.Count);

            foreach (var withdrawal in pendingWithdrawals)
            {
                if (ct.IsCancellationRequested)
                    break;

                try
                {
                    var result = await CreatePayoutAsync(withdrawal.Withdrawalid, ct);

                    if (!result.IsSuccess && result.ShouldRetry)
                    {
                        withdrawal.Retrycount = (withdrawal.Retrycount ?? 0) + 1;
                        withdrawal.Lastretryat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;

                        // Check if reached max retries
                        if (withdrawal.Retrycount >= _settings.MaxRetries)
                        {
                            logger.LogWarning("Withdrawal {WithdrawalId} reached max retries ({MaxRetries}), rejecting. Original error: {ErrorCode}",
                                withdrawal.Withdrawalid, _settings.MaxRetries, result.ErrorCode);

                            // Reject withdrawal - keep original error code, don't overwrite
                            withdrawal.Status = WithdrawalStatus.Rejected;
                            withdrawal.Processedat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;

                            // payosresponsecode and payoserror already set by CreatePayoutAsync
                            await context.SaveChangesAsync(ct);

                            // Create alert for admin
                            await CreatePayoutFailedAlertAsync(withdrawal.Withdrawalid,
                                result.ErrorCode ?? PayoutConstants.ErrorCodes.Unknown,
                                $"Đã retry {_settings.MaxRetries} lần. Lỗi gốc: {result.ErrorMessage}", ct);

                            // Notify tutor with original error message
                            await notificationService.CreateNotificationAsync(new MV.DomainLayer.DTO.RequestModel.NotificationRequest
                            {
                                Userid = withdrawal.Userid!,
                                Title = "Rút tiền thất bại",
                                Message = $"{GetUserFriendlyErrorMessage(result.ErrorCode ?? PayoutConstants.ErrorCodes.Unknown)} Đã thử {_settings.MaxRetries} lần."
                            });

                            // Auto-refund wallet — balance was deducted at withdrawal creation
                            await RefundWithdrawalToWalletAsync(withdrawal.Withdrawalid,
                                $"Hết số lần retry ({_settings.MaxRetries}): {result.ErrorMessage}", ct);
                        }
                        else
                        {
                            await context.SaveChangesAsync(ct);

                            logger.LogInformation("Payout retry scheduled: Withdrawal={WithdrawalId}, RetryCount={RetryCount}",
                                withdrawal.Withdrawalid, withdrawal.Retrycount);
                        }
                    }

                    await Task.Delay(_settings.PayoutDelayMilliseconds, ct);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error processing payout for withdrawal {WithdrawalId}", withdrawal.Withdrawalid);
                }
            }

            logger.LogInformation("Completed pending payouts processing");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in ProcessPendingPayoutsAsync");
        }
    }

    private async Task HandlePayoutErrorAsync(Withdrawalrequest withdrawal, string errorCode, string errorMessage, CancellationToken ct)
    {
        withdrawal.Payosresponsecode = errorCode;
        withdrawal.Payoserror = errorMessage;
        withdrawal.Payosstatus = PayoutConstants.PayOSStatus.Failed;

        var isTerminal = !ShouldRetryError(errorCode);

        if (isTerminal)
        {
            withdrawal.Status = WithdrawalStatus.Rejected;
            withdrawal.Processedat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;

            await CreatePayoutFailedAlertAsync(withdrawal.Withdrawalid, errorCode, errorMessage, ct);

            await notificationService.CreateNotificationAsync(new MV.DomainLayer.DTO.RequestModel.NotificationRequest
            {
                Userid = withdrawal.Userid!,
                Title = "Rút tiền thất bại",
                Message = GetUserFriendlyErrorMessage(errorCode)
            });
        }

        await context.SaveChangesAsync(ct);

        // Auto-refund on terminal failure. Wrapped so a refund failure can't bubble into
        // CreatePayoutAsync's generic catch (which would re-run this handler with a misleading code).
        if (isTerminal)
        {
            try
            {
                await RefundWithdrawalToWalletAsync(withdrawal.Withdrawalid, errorMessage, ct);
            }
            catch (Exception ex)
            {
                logger.LogCritical(ex,
                    "Failed to auto-refund wallet for terminally-rejected withdrawal {WithdrawalId}; manual refund required",
                    withdrawal.Withdrawalid);
            }
        }
    }

    private async Task CreateInsufficientBalanceAlertAsync(int withdrawalId, decimal amount, CancellationToken ct)
    {
        var recentAlert = await context.Systemalerts
            .Where(a => a.Type == PayoutConstants.AlertTypes.LowBalance && !a.Resolved
                && a.Createdat >= MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow.AddMinutes(-_settings.AlertDeduplicationWindowMinutes))
            .FirstOrDefaultAsync(ct);

        if (recentAlert is not null)
        {
            logger.LogDebug("Skipping duplicate insufficient balance alert");
            return;
        }

        var alert = new Systemalert
        {
            Type = PayoutConstants.AlertTypes.LowBalance,
            Severity = PayoutConstants.AlertSeverity.Critical,
            Message = $"Số dư PayOS không đủ cho yêu cầu rút tiền {withdrawalId} (số tiền: {amount:N0} VND). Vui lòng nạp tiền vào ví PayOS.",
            Metadata = JsonSerializer.Serialize(new { withdrawalId, amount }),
            Resolved = false,
            Createdat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow
        };

        context.Systemalerts.Add(alert);
        await context.SaveChangesAsync(ct);

        logger.LogCritical("Insufficient balance alert created for withdrawal {WithdrawalId}", withdrawalId);
    }

    private async Task CreatePayoutFailedAlertAsync(int withdrawalId, string errorCode, string errorMessage, CancellationToken ct)
    {
        var alert = new Systemalert
        {
            Type = PayoutConstants.AlertTypes.PayoutFailed,
            Severity = PayoutConstants.AlertSeverity.Critical,
            Message = $"Rút tiền thất bại cho yêu cầu {withdrawalId}: {errorMessage}",
            Metadata = JsonSerializer.Serialize(new { withdrawalId, errorCode, errorMessage }),
            Resolved = false,
            Createdat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow
        };

        context.Systemalerts.Add(alert);
        await context.SaveChangesAsync(ct);

        logger.LogError("Payout failed alert created: Withdrawal={WithdrawalId}, Error={ErrorCode}", withdrawalId, errorCode);
    }

    private static bool ShouldRetryError(string? errorCode) => errorCode switch
    {
        PayoutConstants.ErrorCodes.Timeout => true,
        PayoutConstants.ErrorCodes.NetworkError => true,
        PayoutConstants.ErrorCodes.InsufficientBalance => true,
        PayoutConstants.ErrorCodes.InvalidBankAccount => false,
        PayoutConstants.ErrorCodes.DuplicateOrder => false,
        _ => true
    };

    private int CalculateRetryDelay(int retryCount, string? errorCode) =>
        errorCode == PayoutConstants.ErrorCodes.InsufficientBalance
            ? _settings.InsufficientBalanceRetryDelayMinutes
            : _settings.RetryDelayMinutes * (int)Math.Pow(_settings.RetryDelayMultiplier, retryCount);

    public async Task<MV.DomainLayer.DTO.ResponseModel.Admin.PayOSBalanceResponse?> GetPayOSBalanceAsync(CancellationToken ct = default)
    {
        try
        {
            var accountInfo = await _payOSClient.PayoutsAccount.GetBalanceAsync(
                new PayOS.Models.RequestOptions { CancellationToken = ct });

            if (accountInfo is null)
            {
                logger.LogWarning("Failed to retrieve PayOS account balance");
                return null;
            }

            if (!decimal.TryParse(accountInfo.Balance, out var balance))
            {
                logger.LogWarning("Failed to parse PayOS balance: {Balance}", accountInfo.Balance);
                return null;
            }

            var alertLevel = balance switch
            {
                < 1_000_000 => PayoutConstants.AlertSeverity.Critical,
                < 10_000_000 => PayoutConstants.AlertSeverity.Warning,
                _ => PayoutConstants.AlertSeverity.Normal
            };

            logger.LogInformation("PayOS balance retrieved: {Balance} VND, Alert level: {AlertLevel}",
                balance, alertLevel);

            return new MV.DomainLayer.DTO.ResponseModel.Admin.PayOSBalanceResponse
            {
                Balance = balance,
                LastChecked = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow,
                AlertLevel = alertLevel
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting PayOS balance");
            return null;
        }
    }

    private static string GetUserFriendlyErrorMessage(string errorCode) => errorCode switch
    {
        PayoutConstants.ErrorCodes.InsufficientBalance => PayoutConstants.ErrorMessages.InsufficientBalance,
        PayoutConstants.ErrorCodes.InvalidBankAccount => PayoutConstants.ErrorMessages.InvalidBankAccount,
        PayoutConstants.ErrorCodes.DuplicateOrder => PayoutConstants.ErrorMessages.DuplicateOrder,
        PayoutConstants.ErrorCodes.Timeout => PayoutConstants.ErrorMessages.Timeout,
        PayoutConstants.ErrorCodes.NetworkError => PayoutConstants.ErrorMessages.NetworkError,
        PayoutConstants.ErrorCodes.MaxRetriesExceeded => PayoutConstants.ErrorMessages.MaxRetriesExceeded,
        _ => PayoutConstants.ErrorMessages.Unknown
    };

    public async Task<bool> RefundWithdrawalToWalletAsync(int withdrawalId, string reason, CancellationToken ct = default)
    {
        await using var tx = await context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        try
        {
            var withdrawal = await context.Withdrawalrequests
                .AsNoTracking()
                .FirstOrDefaultAsync(w => w.Withdrawalid == withdrawalId, ct);

            if (withdrawal is null)
            {
                logger.LogWarning("RefundWithdrawalToWallet: withdrawal {WithdrawalId} not found", withdrawalId);
                await tx.RollbackAsync(ct);
                return false;
            }

            // Guard: transfer already succeeded — never refund if money was sent
            if (withdrawal.Payosstatus == PayoutConstants.PayOSStatus.Success
                || withdrawal.Status == WithdrawalStatus.Approved
                || withdrawal.Status == WithdrawalStatus.Completed)
            {
                logger.LogWarning(
                    "RefundWithdrawalToWallet: withdrawal {WithdrawalId} already succeeded " +
                    "(status={Status}, payosstatus={PayosStatus}), skipping refund",
                    withdrawalId, withdrawal.Status, withdrawal.Payosstatus);
                await tx.RollbackAsync(ct);
                return false;
            }

            // Guard: idempotent — skip if a refund transaction already exists
            var alreadyRefunded = await context.Wallettransactions
                .AnyAsync(wt => wt.Referencetable == ReferenceTable.Withdrawal
                    && wt.Referenceid == withdrawalId
                    && wt.Transactiontype == TransactionType.Refund, ct);

            if (alreadyRefunded)
            {
                logger.LogWarning("RefundWithdrawalToWallet: withdrawal {WithdrawalId} already refunded, skipping", withdrawalId);
                await tx.RollbackAsync(ct);
                return false;
            }

            var wallet = await context.Wallets
                .FromSqlRaw(SqlQueries.LockWalletByUserId, withdrawal.Userid)
                .FirstOrDefaultAsync(ct);

            if (wallet is null)
            {
                logger.LogError("RefundWithdrawalToWallet: wallet not found for user {UserId}, withdrawal {WithdrawalId}",
                    withdrawal.Userid, withdrawalId);
                await tx.RollbackAsync(ct);
                return false;
            }

            var amount = withdrawal.Amount ?? 0;
            wallet.Balance = (wallet.Balance ?? 0) + amount;
            wallet.Lastupdated = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;

            context.Wallettransactions.Add(new Wallettransaction
            {
                Walletid = wallet.Walletid,
                Amount = amount,
                Transactiontype = TransactionType.Refund,
                Referencetable = ReferenceTable.Withdrawal,
                Referenceid = withdrawalId,
                Description = $"Auto-refund for failed withdrawal #{withdrawalId}: {reason}",
                Createdat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow
            });

            await context.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            logger.LogInformation("Refunded {Amount} VND to wallet for withdrawal {WithdrawalId}: {Reason}",
                amount, withdrawalId, reason);

            // Best-effort: refund is already committed; a notification failure must NOT throw
            // (it would trigger a rollback of the committed tx and falsely signal refund failure).
            try
            {
                await notificationService.CreateNotificationAsync(new MV.DomainLayer.DTO.RequestModel.NotificationRequest
                {
                    Userid = withdrawal.Userid!,
                    Title = "Rút tiền thất bại — Đã hoàn tiền về ví",
                    Message = $"Yêu cầu rút tiền {amount:N0} VND không thể xử lý. Số tiền đã được hoàn về ví của bạn."
                });
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Refund succeeded but failed to notify user for withdrawal {WithdrawalId}", withdrawalId);
            }

            return true;
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    private async Task CreateStuckPayoutAlertAsync(int withdrawalId, decimal amount, CancellationToken ct)
    {
        var existingAlert = await context.Systemalerts
            .AnyAsync(a => a.Type == PayoutConstants.AlertTypes.StuckPayout
                && a.Metadata!.Contains(withdrawalId.ToString())
                && !a.Resolved, ct);

        if (existingAlert)
        {
            logger.LogDebug("Skipping duplicate StuckPayout alert for withdrawal {WithdrawalId}", withdrawalId);
            return;
        }

        var alert = new Systemalert
        {
            Type = PayoutConstants.AlertTypes.StuckPayout,
            Severity = PayoutConstants.AlertSeverity.Critical,
            Message = $"Rút tiền #{withdrawalId} có thể đã được gửi đến PayOS nhưng txid chưa được lưu (DuplicateOrder). Admin cần tra PayOS dashboard và cập nhật txid thủ công.",
            Metadata = JsonSerializer.Serialize(new { withdrawalId, amount }),
            Resolved = false,
            Createdat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow
        };

        context.Systemalerts.Add(alert);
        await context.SaveChangesAsync(ct);

        logger.LogError("StuckPayout alert created for withdrawal {WithdrawalId}", withdrawalId);
    }
}
