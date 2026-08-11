using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MV.ApplicationLayer.Helpers;
using MV.ApplicationLayer.ServiceInterfaces;
using PayOS;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.Entities;
using MV.DomainLayer.Exceptions;
using MV.DomainLayer.Helpers;
using MV.ApplicationLayer.Interfaces;

namespace MV.ApplicationLayer.Services;

public class WalletService(
    IAppDbContext context,
    [FromKeyedServices(ServiceKeys.PayOS.Checkout)] PayOSClient payOS,
    INotificationService notificationService,
    IFileStorageService fileStorageService,
    ILogger<WalletService> logger) : IWalletService
{
    private readonly PayOSClient _payOS = payOS;
    private const decimal MinWithdrawalAmount = 10000m;

    public async Task ProcessTopupWebhookAsync(
        PaymentWebhookRequest request,
        string rawPayload,
        CancellationToken ct = default)
    {
        if (request?.Data == null)
            throw new BookingException(WalletErrorCodes.InvalidAmount, "Dữ liệu webhook không hợp lệ", 400);

        if (request.Code != PayOSWebhookCode.SuccessCode || !request.Success)
        {
            logger.LogWarning("Non-success topup webhook: code={Code}", request.Code);
            return;
        }

        var data = request.Data;
        logger.LogInformation("Processing topup webhook orderCode: {OrderCode}, amount: {Amount}", data.OrderCode, data.Amount);

        await using var tx = await context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        try
        {
            var topup = await context.Topuprequests
                .FirstOrDefaultAsync(t => t.Ordercode == data.OrderCode, ct)
                ?? throw new BookingException(WalletErrorCodes.TopupNotFound, "Không tìm thấy yêu cầu nạp bù của booking.", 404);

            if (topup.Bookingid is null
                || topup.Paymentphase is not (
                    PaymentRequestPhase.Deposit or PaymentRequestPhase.Remaining)
                || string.IsNullOrWhiteSpace(topup.Userid)
                || !topup.Amount.HasValue
                || topup.Amount.Value <= 0)
            {
                throw new BookingException(
                    WalletErrorCodes.TopupNotFound,
                    "Top-up truyền thống không còn được hỗ trợ.",
                    409);
            }

            if (data.Amount != (int)(topup.Amount ?? 0))
                throw new BookingException(WalletErrorCodes.AmountMismatch, "Số tiền không khớp", 409);

            var capture = PaymentTransactionCapture.FromPayOSWebhook(
                request,
                rawPayload);
            var providerTransactionId = capture.GetProviderTransactionId(data.Reference);

            var walletTransactionExists = await context.Wallettransactions
                .AsNoTracking()
                .AnyAsync(w => w.Ordercode == data.OrderCode
                    && w.Referencetable == ReferenceTable.Topup, ct);

            var paymentTransactionExists = !string.IsNullOrWhiteSpace(providerTransactionId)
                ? await context.PaymentTransactions.AsNoTracking().AnyAsync(t =>
                    t.Paymentmethod == PaymentTransactionMethod.PayOS
                    && t.Providertransactionid == providerTransactionId, ct)
                : await context.PaymentTransactions.AsNoTracking().AnyAsync(t =>
                    t.Purpose == PaymentTransactionPurpose.WalletTopup
                    && t.Ordercode == data.OrderCode, ct);

            if (topup.Status == TopupStatus.Completed
                && walletTransactionExists
                && paymentTransactionExists)
            {
                await tx.CommitAsync(ct);
                logger.LogInformation(
                    "Booking shortfall top-up {OrderCode} was already fully processed",
                    data.OrderCode);
                return;
            }

            if (!walletTransactionExists && topup.Status != TopupStatus.Completed)
            {
                var wallet = await context.Wallets
                    .FromSqlRaw(SqlQueries.LockWalletByUserId, topup.Userid)
                    .FirstOrDefaultAsync(ct)
                    ?? new Wallet
                    {
                        Userid = topup.Userid,
                        Balance = 0,
                        Frozenbalance = 0,
                        Lastupdated = TimeZoneHelper.UtcNow
                    };

                if (wallet.Walletid == 0)
                    context.Wallets.Add(wallet);

                wallet.Balance = (wallet.Balance ?? 0) + topup.Amount;
                wallet.Lastupdated = TimeZoneHelper.UtcNow;

                context.Wallettransactions.Add(new Wallettransaction
                {
                    Wallet = wallet,
                    Amount = topup.Amount,
                    Transactiontype = TransactionType.Deposit,
                    Referencetable = ReferenceTable.Topup,
                    Referenceid = topup.Topuprequestid,
                    Description = data.Reference,
                    Ordercode = data.OrderCode,
                    Createdat = TimeZoneHelper.UtcNow
                });
            }
            else if (!walletTransactionExists)
            {
                logger.LogWarning(
                    "Completed booking shortfall top-up {OrderCode} has no wallet ledger row; skipping balance mutation to avoid a double credit",
                    data.OrderCode);
            }

            if (topup.Status != TopupStatus.Completed)
            {
                topup.Status = TopupStatus.Completed;
                topup.Completedat = TimeZoneHelper.UtcNow;
            }

            if (!paymentTransactionExists)
            {
                context.PaymentTransactions.Add(capture.Create(
                    PaymentTransactionPurpose.WalletTopup,
                    PaymentTransactionDirection.Inbound,
                    topup.Amount ?? data.Amount,
                    topup.Userid,
                    data.OrderCode,
                    data.Reference,
                    bookingId: topup.Bookingid,
                    description: "Booking shortfall wallet top-up payment"));
            }

            await context.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            logger.LogInformation(
                "Booking shortfall top-up {OrderCode} reconciled for user {UserId}; walletLedgerExisted={WalletLedgerExisted}, paymentAuditExisted={PaymentAuditExisted}",
                data.OrderCode,
                topup.Userid,
                walletTransactionExists,
                paymentTransactionExists);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    public async Task<WalletBalanceResponse> GetWalletBalanceAsync(string userId)
    {
        var w = await context.Wallets.AsNoTracking()
            .FirstOrDefaultAsync(w => w.Userid == userId);

        var bal = w?.Balance ?? 0;
        var frz = w?.Frozenbalance ?? 0;

        return new WalletBalanceResponse
        {
            Balance = bal,
            AvailableBalance = bal,
            FrozenBalance = frz,
            TotalBalance = bal + frz,
            LastUpdated = w != null ? w.Lastupdated : null
        };
    }

    public async Task<TransactionHistoryPagedResponse> GetTransactionHistoryAsync(
        string userId, int page = 1, int pageSize = 20, string? type = null, DateTime? from = null, DateTime? to = null)
    {
        var w = await context.Wallets.AsNoTracking()
            .FirstOrDefaultAsync(w => w.Userid == userId);

        if (w == null)
            return new TransactionHistoryPagedResponse
            {
                Transactions = [],
                TotalCount = 0,
                Page = page,
                PageSize = pageSize
            };

        var query = context.Wallettransactions.AsNoTracking()
            .Where(t => t.Walletid == w.Walletid);

        if (!string.IsNullOrEmpty(type))
            query = query.Where(t => t.Transactiontype == type);

        if (from.HasValue)
        {
            var fromUtc = from.Value.Kind == DateTimeKind.Utc
                ? from.Value
                : DateTime.SpecifyKind(from.Value, DateTimeKind.Utc);
            query = query.Where(t => t.Createdat >= fromUtc);
        }
        if (to.HasValue)
        {
            var toUtc = to.Value.Kind == DateTimeKind.Utc
                ? to.Value
                : DateTime.SpecifyKind(to.Value, DateTimeKind.Utc);
            query = query.Where(t => t.Createdat <= toUtc);
        }

        var orderedQuery = query.OrderByDescending(t => t.Createdat);

        var total = await orderedQuery.CountAsync();
        var rawTxs = await orderedQuery
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(t => new { t.Transactionid, t.Amount, t.Transactiontype, t.Description, t.Referenceid, t.Referencetable, t.Createdat })
            .ToListAsync();

        var txs = rawTxs.Select(t => new TransactionHistoryResponse
        {
            TransactionId = t.Transactionid,
            Amount = t.Amount ?? 0,
            TransactionType = t.Transactiontype ?? "",
            Description = t.Description ?? "",
            ReferenceId = t.Referenceid,
            ReferenceTable = t.Referencetable,
            CreatedAt = t.Createdat ?? MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow
        }).ToList();

        return new TransactionHistoryPagedResponse
        {
            Transactions = txs,
            TotalCount = total,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<TransactionDetailResponse> GetTransactionDetailAsync(string userId, int transactionId, CancellationToken ct = default)
    {
        var wallet = await context.Wallets.AsNoTracking()
            .FirstOrDefaultAsync(w => w.Userid == userId, ct)
            ?? throw new WalletNotFoundException();

        var tx = await context.Wallettransactions.AsNoTracking()
            .Where(t => t.Transactionid == transactionId && t.Walletid == wallet.Walletid)
            .Select(t => new { t.Transactionid, t.Amount, t.Transactiontype, t.Description, t.Referenceid, t.Referencetable, t.Createdat })
            .FirstOrDefaultAsync(ct)
            ?? throw new TransactionNotFoundException();

        var detail = new TransactionDetailResponse
        {
            TransactionId = tx.Transactionid,
            Amount = tx.Amount ?? 0,
            TransactionType = tx.Transactiontype ?? "",
            Description = tx.Description ?? "",
            ReferenceId = tx.Referenceid,
            ReferenceTable = tx.Referencetable,
            CreatedAt = tx.Createdat ?? MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow
        };

        if (tx.Referenceid is int refId)
        {
            switch (tx.Referencetable)
            {
                case ReferenceTable.Booking:
                    detail.Booking = await BuildBookingInvoiceAsync(refId, ct);
                    break;
                case ReferenceTable.Withdrawal:
                    detail.Withdrawal = await BuildWithdrawalDetailAsync(refId, ct);
                    // Chứng từ chi trả (mã provider + ảnh) — giữ từ luồng rút tiền của develop.
                    var proof = await PayoutProofResolver.ResolveAsync(context, fileStorageService, refId, ct);
                    detail.ProviderTransactionId = proof.ProviderTransactionId;
                    detail.PaidAt = proof.PaidAt;
                    detail.ProofImageUrl = proof.ProofImageUrl;
                    break;
                // "dispute" chưa nằm trong ReferenceTable constants nhưng có thể xuất hiện
                // ở dữ liệu — so khớp không phân biệt hoa thường cho chắc.
                default:
                    if (string.Equals(tx.Referencetable, "dispute", StringComparison.OrdinalIgnoreCase))
                        detail.Dispute = await BuildDisputeDetailAsync(refId, ct);
                    break;
            }
        }

        return detail;
    }

    private async Task<BookingInvoiceDetail?> BuildBookingInvoiceAsync(int bookingId, CancellationToken ct)
    {
        var b = await context.Bookings.AsNoTracking()
            .Where(x => x.Bookingid == bookingId)
            .Select(x => new
            {
                x.Bookingid,
                x.Tutorid,
                x.Studentid,
                x.Tutorsubjectgradepriceid,
                x.Totalsessions,
                x.Priceperhour,
                x.Totalamount,
                x.Depositamount,
                x.Remainingamount,
                x.Status,
                x.Paymentstatus,
                x.Startdate,
                x.Createdat
            })
            .FirstOrDefaultAsync(ct);

        if (b == null) return null;

        var tutorName = b.Tutorid == null ? null : await context.Users.AsNoTracking()
            .Where(u => u.Userid == b.Tutorid).Select(u => u.Fullname).FirstOrDefaultAsync(ct);
        var studentName = b.Studentid == null ? null : await context.Users.AsNoTracking()
            .Where(u => u.Userid == b.Studentid).Select(u => u.Fullname).FirstOrDefaultAsync(ct);

        string? subjectName = null;
        if (b.Tutorsubjectgradepriceid is int tsgpId)
        {
            subjectName = await context.Tutorsubjectgradeprices.AsNoTracking()
                .Where(t => t.Id == tsgpId)
                .Select(t => t.Subject != null ? t.Subject.Subjectname : null)
                .FirstOrDefaultAsync(ct);
        }

        return new BookingInvoiceDetail
        {
            BookingId = b.Bookingid,
            SubjectName = subjectName,
            TutorName = tutorName,
            StudentName = studentName,
            TotalSessions = b.Totalsessions,
            PricePerHour = b.Priceperhour,
            TotalAmount = b.Totalamount,
            DepositAmount = b.Depositamount,
            RemainingAmount = b.Remainingamount,
            Status = b.Status,
            PaymentStatus = b.Paymentstatus,
            StartDate = b.Startdate,
            CreatedAt = b.Createdat
        };
    }

    private async Task<DisputeTransactionDetail?> BuildDisputeDetailAsync(int disputeId, CancellationToken ct)
    {
        var d = await context.Disputes.AsNoTracking()
            .Where(x => x.Disputeid == disputeId)
            .Select(x => new
            {
                x.Disputeid,
                x.Bookingid,
                x.Disputetype,
                x.Reason,
                x.Status,
                x.Resolutionnote,
                x.Refundamount,
                x.Refundpercentage,
                x.Createdat,
                x.Resolvedat
            })
            .FirstOrDefaultAsync(ct);

        if (d == null) return null;

        return new DisputeTransactionDetail
        {
            DisputeId = d.Disputeid,
            BookingId = d.Bookingid,
            DisputeType = d.Disputetype,
            Reason = d.Reason,
            Status = d.Status,
            ResolutionNote = d.Resolutionnote,
            RefundAmount = d.Refundamount,
            RefundPercentage = d.Refundpercentage,
            CreatedAt = d.Createdat,
            ResolvedAt = d.Resolvedat
        };
    }

    private async Task<WithdrawalTransactionDetail?> BuildWithdrawalDetailAsync(int withdrawalId, CancellationToken ct)
    {
        var w = await context.Withdrawalrequests.AsNoTracking()
            .Where(x => x.Withdrawalid == withdrawalId)
            .Select(x => new
            {
                x.Withdrawalid,
                x.Amount,
                x.Status,
                x.Bankname,
                x.Accountnumber,
                x.Accountholdername,
                x.Requestedat,
                x.Processedat,
                x.Completionnote
            })
            .FirstOrDefaultAsync(ct);

        if (w == null) return null;

        return new WithdrawalTransactionDetail
        {
            WithdrawalId = w.Withdrawalid,
            Amount = w.Amount ?? 0,
            Status = w.Status,
            BankName = w.Bankname,
            AccountNumber = w.Accountnumber,
            AccountHolderName = w.Accountholdername,
            RequestedAt = w.Requestedat,
            ProcessedAt = w.Processedat,
            CompletionNote = w.Completionnote
        };
    }

    /// <summary>
    /// Parent/Student withdrawal creation — bank destination is the requester's saved
    /// BankAccount (same as Tutor's own flow, see TutorFinanceService.CreateWithdrawalAsync),
    /// snapshotted onto the withdrawal row so it survives later bank-account edits/deletes.
    /// Typing bank details per-withdrawal was removed; saving/changing the payout destination
    /// now always goes through the OTP-gated bank-account flow (BankAccountController).
    /// </summary>
    public async Task<WithdrawalDetailResponse> CreateWithdrawalAsync(
        string userId, CreateWithdrawalRequest request, CancellationToken ct = default)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var committed = false;

        try
        {
            var wallet = await context.Wallets
                .FromSqlRaw(SqlQueries.LockWalletByUserId, userId)
                .FirstOrDefaultAsync(ct);

            if (wallet == null)
                throw new WalletNotFoundException();

            if ((wallet.Balance ?? 0) < request.Amount)
                throw new InsufficientBalanceException();

            var pendingWithdrawal = await context.Withdrawalrequests
                .AnyAsync(w => w.Userid == userId
                               && (w.Status == WithdrawalStatus.Pending
                                   || w.Status == WithdrawalStatus.Delayed
                                   || w.Status == WithdrawalStatus.PendingReview
                                   || w.Status == WithdrawalStatus.Approved), ct);

            if (pendingWithdrawal)
                throw new PendingWithdrawalException();

            var bankAccount = await context.BankAccounts
                .AsNoTracking()
                .FirstOrDefaultAsync(b => b.Userid == userId, ct);

            if (bankAccount == null
                || string.IsNullOrEmpty(bankAccount.Bankname)
                || string.IsNullOrEmpty(bankAccount.Accountnumber)
                || string.IsNullOrEmpty(bankAccount.Accountholdername))
                throw new BankInfoRequiredException();

            if (request.Amount < MinWithdrawalAmount)
                throw new WithdrawalAmountTooLowException(MinWithdrawalAmount);

            wallet.Balance -= request.Amount;
            wallet.Lastupdated = TimeZoneHelper.UtcNow;

            var withdrawal = new Withdrawalrequest
            {
                Userid = userId,
                Walletid = wallet.Walletid,
                Amount = request.Amount,
                Bankname = bankAccount.Bankname,
                Accountnumber = bankAccount.Accountnumber,
                Accountholdername = bankAccount.Accountholdername,
                Status = WithdrawalStatus.PendingReview,
                Decision = TrustScoringConstants.Decisions.ManualReview,
                Requestedat = TimeZoneHelper.UtcNow
            };

            context.Withdrawalrequests.Add(withdrawal);

            var walletTransaction = new Wallettransaction
            {
                Walletid = wallet.Walletid,
                Amount = -request.Amount,
                Transactiontype = TransactionType.Withdrawal,
                Referencetable = ReferenceTable.Withdrawal,
                Description = "Withdrawal request",
                Createdat = TimeZoneHelper.UtcNow
            };

            context.Wallettransactions.Add(walletTransaction);

            await context.SaveChangesAsync(ct);

            walletTransaction.Referenceid = withdrawal.Withdrawalid;
            walletTransaction.Description = $"Withdrawal request #{withdrawal.Withdrawalid}";
            await context.SaveChangesAsync(ct);

            await transaction.CommitAsync(ct);
            committed = true;

            const string notificationMessage = "Yêu cầu rút tiền của bạn đã được ghi nhận và đang chờ admin/staff xét duyệt, dự kiến trong vòng 24 giờ.";

            try
            {
                await notificationService.CreateNotificationAsync(new NotificationRequest
                {
                    Userid = userId,
                    Title = "Yêu cầu rút tiền đã được tạo",
                    Message = notificationMessage,
                    Type = NotificationType.WithdrawalRequest,
                    Referenceid = withdrawal.Withdrawalid.ToString()
                });
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to send notification for withdrawal {WithdrawalId}", withdrawal.Withdrawalid);
            }

            logger.LogInformation(
                "Created withdrawal {WithdrawalId} for user {UserId}, amount: {Amount}",
                withdrawal.Withdrawalid, userId, request.Amount);

            return new WithdrawalDetailResponse
            {
                WithdrawalId = withdrawal.Withdrawalid,
                Amount = withdrawal.Amount ?? 0,
                Status = withdrawal.Status ?? string.Empty,
                BankName = withdrawal.Bankname,
                AccountNumber = withdrawal.Accountnumber,
                AccountHolderName = withdrawal.Accountholdername,
                RequestedAt = withdrawal.Requestedat ?? TimeZoneHelper.UtcNow,
                ProcessedAt = withdrawal.Processedat
            };
        }
        catch
        {
            if (!committed)
                await transaction.RollbackAsync(ct);
            throw;
        }
    }

    public async Task<WithdrawalListResponse> GetWithdrawalsAsync(
        string userId, int page, int pageSize, CancellationToken ct = default)
    {
        var query = context.Withdrawalrequests.AsNoTracking().Where(w => w.Userid == userId);

        var total = await query.CountAsync(ct);

        var rawItems = await query
            .OrderByDescending(w => w.Requestedat)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(w => new { w.Withdrawalid, w.Amount, w.Status, w.Requestedat, w.Processedat })
            .ToListAsync(ct);

        var items = rawItems.Select(w => new WithdrawalItem
        {
            WithdrawalId = w.Withdrawalid,
            Amount = w.Amount ?? 0,
            Status = w.Status ?? string.Empty,
            RequestedAt = w.Requestedat ?? TimeZoneHelper.UtcNow,
            ProcessedAt = w.Processedat
        }).ToList();

        return new WithdrawalListResponse
        {
            Items = items,
            Total = total,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<WithdrawalDetailResponse> GetWithdrawalDetailAsync(
        string userId, int withdrawalId, CancellationToken ct = default)
    {
        var raw = await context.Withdrawalrequests
            .AsNoTracking()
            .Where(w => w.Withdrawalid == withdrawalId && w.Userid == userId)
            .Select(w => new
            {
                w.Withdrawalid,
                w.Amount,
                w.Status,
                w.Bankname,
                w.Accountnumber,
                w.Accountholdername,
                w.Requestedat,
                w.Processedat,
                w.Claimedat,
                w.Completionnote,
                w.Rejectionreason
            })
            .FirstOrDefaultAsync(ct);

        if (raw == null)
            throw new WithdrawalNotFoundException();

        var proof = await PayoutProofResolver.ResolveAsync(context, fileStorageService, withdrawalId, ct);

        return new WithdrawalDetailResponse
        {
            WithdrawalId = raw.Withdrawalid,
            Amount = raw.Amount ?? 0,
            Status = raw.Status ?? string.Empty,
            BankName = raw.Bankname,
            AccountNumber = raw.Accountnumber,
            AccountHolderName = raw.Accountholdername,
            RequestedAt = raw.Requestedat ?? TimeZoneHelper.UtcNow,
            ProcessedAt = raw.Processedat,
            ClaimedAt = raw.Claimedat,
            CompletionNote = raw.Completionnote,
            RejectionReason = raw.Rejectionreason,
            TransactionId = proof.ProviderTransactionId,
            PaidAt = proof.PaidAt,
            ProofImageUrl = proof.ProofImageUrl
        };
    }

    public async Task<TopupStatusResponse> GetBookingShortfallTopupStatusAsync(
        int bookingId,
        long orderCode,
        string userId,
        CancellationToken ct = default)
    {
        var topup = await context.Topuprequests.AsNoTracking()
            .FirstOrDefaultAsync(t =>
                t.Bookingid == bookingId
                && t.Ordercode == orderCode
                && t.Userid == userId,
                ct)
            ?? throw new BookingException(
                WalletErrorCodes.TopupNotFound,
                "Không tìm thấy yêu cầu nạp bù của booking.",
                404);

        if (topup.Status == TopupStatus.Completed)
        {
            return await BuildTopupStatusResponseAsync(
                topup,
                TopupStatus.Completed,
                ct);
        }

        if (string.IsNullOrWhiteSpace(topup.Paymentlinkid))
        {
            return await BuildTopupStatusResponseAsync(
                topup,
                topup.Status ?? TopupStatus.Pending,
                ct);
        }

        try
        {
            var info = await _payOS.PaymentRequests.GetAsync(topup.Paymentlinkid);
            var payosStatus = info.Status.ToString();

            if (payosStatus.Equals(
                PayOSLinkStatus.Paid,
                StringComparison.OrdinalIgnoreCase))
            {
                var capture = PaymentTransactionCapture
                    .FromPayOSPaymentLink(info)
                    .FirstOrDefault(c => c.ObservedAmount == (topup.Amount ?? 0));

                if (capture != null)
                {
                    var synthetic = new PaymentWebhookRequest
                    {
                        Code = PayOSWebhookCode.SuccessCode,
                        Desc = PayOSWebhookCode.SuccessDesc,
                        Success = true,
                        Data = new PayOSWebhookData
                        {
                            OrderCode = orderCode,
                            Amount = checked((int)(topup.Amount ?? 0)),
                            Reference = capture.ProviderTransactionId
                                ?? $"payos-link-{info.Id}",
                            PaymentLinkId = topup.Paymentlinkid,
                            Description = capture.Description
                                ?? $"Bu booking #{bookingId}",
                            TransactionDateTime = capture.PaidAtUtc?.ToString("O")
                                ?? "",
                            Currency = capture.Currency ?? Currency.Vnd
                        }
                    };

                    await ProcessTopupWebhookAsync(
                        synthetic,
                        System.Text.Json.JsonSerializer.Serialize(info),
                        ct);
                }
                else
                {
                    logger.LogWarning(
                        "Paid PayOS link {PaymentLinkId} for booking shortfall top-up {OrderCode} has no exact transaction capture yet",
                        topup.Paymentlinkid,
                        orderCode);
                }
            }

            var refreshed = await context.Topuprequests.AsNoTracking()
                .FirstOrDefaultAsync(t =>
                    t.Bookingid == bookingId
                    && t.Ordercode == orderCode
                    && t.Userid == userId,
                    ct)
                ?? topup;
            return await BuildTopupStatusResponseAsync(
                refreshed,
                payosStatus,
                ct);
        }
        catch (OperationCanceledException)
            when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Failed to reconcile booking shortfall top-up {OrderCode}",
                orderCode);
            return await BuildTopupStatusResponseAsync(
                topup,
                topup.Status ?? TopupStatus.Pending,
                ct);
        }
    }

    private async Task<TopupStatusResponse> BuildTopupStatusResponseAsync(
        Topuprequest topup,
        string status,
        CancellationToken ct)
    {
        var wallet = await context.Wallets.AsNoTracking()
            .FirstOrDefaultAsync(w => w.Userid == topup.Userid, ct);
        var walletCredited = topup.Status == TopupStatus.Completed
            && await context.Wallettransactions.AsNoTracking()
                .AnyAsync(t =>
                    t.Ordercode == topup.Ordercode
                    && t.Referencetable == ReferenceTable.Topup,
                    ct);

        return new TopupStatusResponse
        {
            OrderCode = topup.Ordercode,
            BookingId = topup.Bookingid ?? 0,
            PaymentPhase = topup.Paymentphase ?? "",
            Status = status,
            WalletCredited = walletCredited,
            Amount = topup.Amount ?? 0,
            WalletBalance = wallet?.Balance ?? 0
        };
    }
}
