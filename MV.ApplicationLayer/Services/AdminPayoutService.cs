using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MV.ApplicationLayer.Helpers;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.RequestModel.Admin;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.DTO.ResponseModel.Admin;
using MV.DomainLayer.Entities;
using MV.DomainLayer.Helpers;
using MV.ApplicationLayer.Interfaces;
using MV.ApplicationLayer.RepositoryInterfaces;
using static MV.DomainLayer.Constants.ClassSessionStatus;
using static MV.DomainLayer.Constants.TrustScoringConstants;

namespace MV.ApplicationLayer.Services;

public class AdminPayoutService(
    IWithdrawalRepository withdrawalRepo,
    IWalletRepository walletRepo,
    IAppDbContext context,
    INotificationService notificationService,
    IFileStorageService fileStorageService,
    ILogger<AdminPayoutService> logger) : IAdminPayoutService
{
    private static readonly string[] ClaimableStatuses =
    [
        WithdrawalStatus.Pending,
        WithdrawalStatus.PendingReview,
        WithdrawalStatus.Delayed
    ];

    public async Task<PayoutOverviewResponse> GetOverviewAsync(CancellationToken ct = default)
    {
        var now = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;
        var today = now.Date;
        var tomorrow = today.AddDays(1);
        var thisMonth = new DateTime(now.Year, now.Month, 1);

        // Complex analytics query: stays on context
        var monthStats = await context.Withdrawalrequests
            .Where(w => w.Requestedat >= thisMonth)
            .GroupBy(w => 1)
            .Select(g => new
            {
                TotalRequests = g.Count(),
                AutoApproved = g.Count(w => w.Decision == Decisions.AutoApprove
                                         || w.Decision == Decisions.AdminApproved
                                         || w.Decision == Decisions.StaffApproved),
                Delayed = g.Count(w => w.Decision == Decisions.Delayed),
                ManualReview = g.Count(w => w.Decision == Decisions.ManualReview),
                Rejected = g.Count(w => w.Status == WithdrawalStatus.Rejected),
                // Approved means a staff/admin has claimed the request but has not yet recorded
                // a completed bank transfer. Only Completed counts as paid out.
                Completed = g.Count(w => w.Status == WithdrawalStatus.Completed)
            })
            .FirstOrDefaultAsync(ct);

        var todayStats = await context.Withdrawalrequests
            .Where(w => w.Requestedat >= today && w.Requestedat < tomorrow)
            .GroupBy(w => 1)
            .Select(g => new
            {
                TotalRequests = g.Count(),
                AutoApproved = g.Count(w => w.Decision == Decisions.AutoApprove
                                         || w.Decision == Decisions.AdminApproved
                                         || w.Decision == Decisions.StaffApproved),
                Delayed = g.Count(w => w.Decision == Decisions.Delayed),
                ManualReview = g.Count(w => w.Decision == Decisions.ManualReview),
                Rejected = g.Count(w => w.Status == WithdrawalStatus.Rejected)
            })
            .FirstOrDefaultAsync(ct);

        var payoutStats = await context.Withdrawalrequests
            .Where(w => w.Status == WithdrawalStatus.Completed
                        && w.Processedat >= thisMonth)
            .GroupBy(w => 1)
            .Select(g => new
            {
                TotalPayoutToday = g.Where(w => w.Processedat >= today && w.Processedat < tomorrow).Sum(w => w.Amount ?? 0),
                TotalPayoutThisMonth = g.Sum(w => w.Amount ?? 0)
            })
            .FirstOrDefaultAsync(ct);

        var pendingCount = await withdrawalRepo.CountPendingAsync(ct);
        var recentAlertsCount = await context.Systemalerts.CountAsync(a => !a.Resolved && a.Createdat >= today, ct);

        var successRate = monthStats?.TotalRequests > 0
            ? (double)monthStats.Completed / monthStats.TotalRequests * 100
            : 0;

        return new PayoutOverviewResponse
        {
            TodayStats = new TodayStatsResponse
            {
                TotalRequests = todayStats?.TotalRequests ?? 0,
                AutoApproved = todayStats?.AutoApproved ?? 0,
                Delayed = todayStats?.Delayed ?? 0,
                ManualReview = todayStats?.ManualReview ?? 0,
                Rejected = todayStats?.Rejected ?? 0
            },
            ProcessingStats = new ProcessingStatsResponse
            {
                AvgProcessingTime = 0,
                SuccessRate = successRate,
                PendingCount = pendingCount
            },
            FinancialStats = new FinancialStatsResponse
            {
                TotalPayoutToday = payoutStats?.TotalPayoutToday ?? 0,
                TotalPayoutThisMonth = payoutStats?.TotalPayoutThisMonth ?? 0
            },
            DecisionBreakdown = new DecisionBreakdownResponse
            {
                AutoApprove = monthStats?.AutoApproved ?? 0,
                Delayed = monthStats?.Delayed ?? 0,
                ManualReview = monthStats?.ManualReview ?? 0,
                Rejected = monthStats?.Rejected ?? 0
            },
            RecentAlertsCount = recentAlertsCount
        };
    }

    public async Task<PendingReviewResponse> GetPendingReviewAsync(int page, int pageSize, CancellationToken ct = default)
    {
        // Complex multi-join query: stays on context
        var query = context.Withdrawalrequests
            .AsNoTracking()
            .Include(w => w.User)
                .ThenInclude(u => u!.Tutorprofile)
            .Where(w => ClaimableStatuses.Contains(w.Status ?? ""));

        var total = await query.CountAsync(ct);

        var items = await query
            .OrderBy(w => w.Requestedat)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(w => new
            {
                w.Withdrawalid,
                w.Userid,
                TutorName = w.User!.Fullname ?? w.User.Username,
                TutorEmail = w.User.Email,
                w.Amount,
                w.Bankname,
                w.Accountnumber,
                w.Status,
                w.Requestedat
            })
            .ToListAsync(ct);

        var result = items.Select(i => new PendingReviewItem
        {
            WithdrawalId = i.Withdrawalid,
            TutorId = i.Userid ?? "",
            TutorName = i.TutorName ?? "",
            TutorEmail = i.TutorEmail ?? "",
            Amount = i.Amount ?? 0,
            BankName = i.Bankname ?? "",
            AccountNumber = i.Accountnumber ?? "",
            Status = i.Status ?? "",
            RequestedAt = i.Requestedat ?? MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow
        }).ToList();

        return new PendingReviewResponse
        {
            Items = result,
            Total = total,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<WithdrawalListResponse> GetAllRequestsAsync(
        int page,
        int pageSize,
        string? status = null,
        string? search = null,
        DateTime? from = null,
        DateTime? to = null,
        CancellationToken ct = default)
    {
        var query = withdrawalRepo.GetBaseQuery();

        if (!string.IsNullOrWhiteSpace(status))
        {
            var normalizedStatus = status.Trim().ToLowerInvariant();
            query = query.Where(w => w.Status == normalizedStatus);
        }
        if (!string.IsNullOrWhiteSpace(search))
        {
            var searchTerm = search.Trim().ToLowerInvariant();
            query = query.Where(w =>
                (w.Userid != null && w.Userid.ToLower().Contains(searchTerm))
                || (w.Accountholdername != null && w.Accountholdername.ToLower().Contains(searchTerm))
                || (w.Accountnumber != null && w.Accountnumber.ToLower().Contains(searchTerm))
                || (w.User != null && w.User.Fullname != null && w.User.Fullname.ToLower().Contains(searchTerm))
                || (w.User != null && w.User.Email != null && w.User.Email.ToLower().Contains(searchTerm)));
        }
        if (from.HasValue)
            query = query.Where(w => w.Requestedat >= from.Value);
        if (to.HasValue)
            query = query.Where(w => w.Requestedat <= to.Value);

        var total = await query.CountAsync(ct);

        var rawItems = await query
            .OrderByDescending(w => w.Requestedat)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(w => new
            {
                w.Withdrawalid,
                w.Userid,
                TutorName = w.User != null ? w.User.Fullname ?? w.User.Username : null,
                TutorEmail = w.User != null ? w.User.Email : null,
                w.Amount,
                w.Bankname,
                w.Accountnumber,
                w.Status,
                w.Requestedat,
                w.Processedat
            })
            .ToListAsync(ct);

        var items = rawItems.Select(w => new WithdrawalItem
        {
            WithdrawalId = w.Withdrawalid,
            TutorId = w.Userid ?? "",
            TutorName = w.TutorName ?? "",
            TutorEmail = w.TutorEmail ?? "",
            Amount = w.Amount ?? 0,
            BankName = w.Bankname ?? "",
            AccountNumber = w.Accountnumber ?? "",
            Status = w.Status ?? "",
            RequestedAt = w.Requestedat ?? MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow,
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

    public async Task<AdminWithdrawalDetailResponse> GetRequestDetailAsync(int withdrawalId, CancellationToken ct = default)
    {
        var withdrawal = await withdrawalRepo.GetByIdWithUserAsync(withdrawalId, ct);

        if (withdrawal == null)
            throw new KeyNotFoundException("Không tìm thấy yêu cầu rút tiền");

        var tutorId = withdrawal.Userid!;

        var rawPreviousWithdrawals = await context.Withdrawalrequests.AsNoTracking()
            .Where(w => w.Userid == tutorId && w.Withdrawalid != withdrawalId)
            .OrderByDescending(w => w.Requestedat)
            .Take(10)
            .Select(w => new { w.Withdrawalid, w.Amount, w.Status, w.Requestedat })
            .ToListAsync(ct);

        var previousWithdrawals = rawPreviousWithdrawals.Select(w => new PreviousWithdrawalResponse
        {
            WithdrawalId = w.Withdrawalid,
            Amount = w.Amount ?? 0,
            Status = w.Status ?? "",
            RequestedAt = w.Requestedat ?? MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow
        }).ToList();

        var wallet = await walletRepo.GetByUserIdAsNoTrackingAsync(tutorId, ct);

        var completedClassSessions = await context.ClassSessions.AsNoTracking()
            .CountAsync(l => l.Tutorid == tutorId && l.Status == Completed, ct);

        var totalEarnings = await context.Wallettransactions.AsNoTracking()
            .Where(t => t.Wallet!.Userid == tutorId && t.Transactiontype == TransactionType.EscrowRelease)
            .SumAsync(t => t.Amount ?? 0, ct);

        var payoutTransaction = await context.PaymentTransactions
            .AsNoTracking()
            .Where(t => t.Withdrawalid == withdrawalId
                        && t.Purpose == PaymentTransactionPurpose.Withdrawal
                        && t.Status == PaymentTransactionStatus.Succeeded)
            .OrderByDescending(t => t.Paymenttransactionid)
            .Select(t => new
            {
                t.Providertransactionid,
                t.Paidat,
                t.Proofimagepath
            })
            .FirstOrDefaultAsync(ct);

        var proofImageUrl = string.IsNullOrWhiteSpace(payoutTransaction?.Proofimagepath)
            ? null
            : fileStorageService.GenerateSignedUrl(payoutTransaction.Proofimagepath);

        var balance = wallet?.Balance ?? 0;
        var frozenBalance = wallet?.Frozenbalance ?? 0;
        var walletInfo = new WalletInfoResponse
        {
            Balance = balance,
            FrozenBalance = frozenBalance,
            AvailableBalance = balance,
            TotalBalance = balance + frozenBalance
        };

        var user = withdrawal.User!;
        var tutorInfo = new TutorInfoResponse
        {
            TutorId = tutorId,
            Name = user.Fullname ?? user.Username ?? "",
            Email = user.Email,
            Phone = user.Phone,
            AccountAgeDays = (MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow - (user.Createdat ?? MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow)).Days,
            CompletedClassSessions = completedClassSessions,
            TotalEarnings = totalEarnings,
            JoinedAt = user.Createdat ?? MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow
        };

        var timeline = BuildTimeline(withdrawal);

        return new AdminWithdrawalDetailResponse
        {
            RequestInfo = new RequestInfoResponse
            {
                WithdrawalId = withdrawal.Withdrawalid,
                Amount = withdrawal.Amount ?? 0,
                Status = withdrawal.Status ?? "",
                Decision = withdrawal.Decision,
                BankName = withdrawal.Bankname,
                AccountNumber = withdrawal.Accountnumber,
                AccountHolderName = withdrawal.Accountholdername,
                CreatedAt = withdrawal.Requestedat ?? MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow,
                ProcessedAt = withdrawal.Processedat,
                ProcessedBy = withdrawal.Processedby,
                CompletionNote = withdrawal.Completionnote,
                ClaimedBy = withdrawal.Claimedby,
                ClaimedAt = withdrawal.Claimedat,
                RejectionReason = withdrawal.Rejectionreason,
                TransactionId = payoutTransaction?.Providertransactionid,
                PaidAt = payoutTransaction?.Paidat,
                ProofImageUrl = proofImageUrl
            },
            TutorInfo = tutorInfo,
            PreviousWithdrawals = previousWithdrawals,
            WalletInfo = walletInfo,
            Timeline = timeline.OrderBy(t => t.Timestamp).ToList()
        };
    }

    private static List<TimelineEventResponse> BuildTimeline(Withdrawalrequest withdrawal)
    {
        var timeline = new List<TimelineEventResponse>
        {
            new() { Timestamp = withdrawal.Requestedat ?? MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow, Event = "Withdrawal requested", Details = $"Amount: {withdrawal.Amount:N0} VND" }
        };

        if (!string.IsNullOrEmpty(withdrawal.Decision))
            timeline.Add(new() { Timestamp = withdrawal.Requestedat ?? MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow, Event = "Decision made", Details = withdrawal.Decision });

        if (withdrawal.Claimedat.HasValue)
            timeline.Add(new() { Timestamp = withdrawal.Claimedat.Value, Event = "Claimed for processing", Details = $"Claimed by: {withdrawal.Claimedby}" });

        if (withdrawal.Processedat.HasValue)
        {
            var details = withdrawal.Status == WithdrawalStatus.Rejected
                ? $"Status: {withdrawal.Status}. Lý do: {withdrawal.Rejectionreason}"
                : $"Status: {withdrawal.Status}. Ghi chú: {withdrawal.Completionnote}";
            timeline.Add(new() { Timestamp = withdrawal.Processedat.Value, Event = "Processed", Details = details });
        }

        return timeline;
    }

    public async Task<ApproveResult> ClaimRequestAsync(
        int withdrawalId,
        string actorUserId,
        CancellationToken ct = default)
    {
        var claimedAt = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;
        var updated = await context.Withdrawalrequests
            .Where(w => w.Withdrawalid == withdrawalId
                        && ClaimableStatuses.Contains(w.Status ?? ""))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(w => w.Status, WithdrawalStatus.Approved)
                .SetProperty(w => w.Claimedby, actorUserId)
                .SetProperty(w => w.Claimedat, claimedAt), ct);

        if (updated == 0)
        {
            var current = await context.Withdrawalrequests
                .AsNoTracking()
                .Where(w => w.Withdrawalid == withdrawalId)
                .Select(w => new { w.Status, w.Claimedby })
                .FirstOrDefaultAsync(ct);

            if (current == null)
                throw new KeyNotFoundException("Không tìm thấy yêu cầu rút tiền");

            if (current.Status == WithdrawalStatus.Approved)
            {
                var owner = current.Claimedby == actorUserId ? "bạn" : "một nhân viên khác";
                throw new InvalidOperationException($"Yêu cầu đã được {owner} nhận xử lý.");
            }

            throw new InvalidOperationException($"Không thể nhận xử lý yêu cầu có trạng thái: {current.Status}");
        }

        logger.LogInformation("Actor {ActorId} claimed withdrawal {WithdrawalId}", actorUserId, withdrawalId);
        return new ApproveResult
        {
            Success = true,
            Message = "Đã nhận xử lý yêu cầu rút tiền"
        };
    }

    public async Task<ApproveResult> ReleaseRequestAsync(
        int withdrawalId,
        string actorUserId,
        CancellationToken ct = default)
    {
        var updated = await context.Withdrawalrequests
            .Where(w => w.Withdrawalid == withdrawalId
                        && w.Status == WithdrawalStatus.Approved
                        && w.Claimedby == actorUserId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(w => w.Status, WithdrawalStatus.PendingReview)
                .SetProperty(w => w.Claimedby, (string?)null)
                .SetProperty(w => w.Claimedat, (DateTime?)null), ct);

        if (updated == 0)
        {
            var current = await context.Withdrawalrequests
                .AsNoTracking()
                .Where(w => w.Withdrawalid == withdrawalId)
                .Select(w => new { w.Status, w.Claimedby })
                .FirstOrDefaultAsync(ct);

            if (current == null)
                throw new KeyNotFoundException("Không tìm thấy yêu cầu rút tiền");

            throw new InvalidOperationException(
                current.Status == WithdrawalStatus.Approved
                    ? "Chỉ người đang nhận xử lý mới có thể trả yêu cầu về hàng đợi."
                    : $"Không thể trả yêu cầu có trạng thái {current.Status} về hàng đợi.");
        }

        logger.LogInformation("Actor {ActorId} released withdrawal {WithdrawalId}", actorUserId, withdrawalId);
        return new ApproveResult
        {
            Success = true,
            Message = "Đã trả yêu cầu về hàng đợi"
        };
    }

    public async Task<ApproveResult> ApproveRequestAsync(
        int withdrawalId,
        string actorUserId,
        string actorRole,
        ApproveWithdrawalRequest request,
        CancellationToken ct = default)
    {
        if (request == null
            || !request.PaidAt.HasValue
            || string.IsNullOrWhiteSpace(request.Note)
            || request.ProofImage == null
            || request.ProofImage.Length == 0)
            throw new InvalidOperationException(
                "Vui lòng nhập thời gian chuyển khoản, ghi chú và ảnh biên lai.");

        // Payout reference is minted by the backend, not typed in by staff/admin — it is an
        // internal audit code (not a bank-provided trace number).
        var transactionId = PayoutCodeGenerator.Generate(withdrawalId);
        var note = request.Note.Trim();
        var preview = await context.Withdrawalrequests
            .AsNoTracking()
            .Where(w => w.Withdrawalid == withdrawalId)
            .Select(w => new { w.Status, w.Claimedby, w.Requestedat })
            .FirstOrDefaultAsync(ct);

        if (preview == null)
            throw new KeyNotFoundException("Không tìm thấy yêu cầu rút tiền");

        if (preview.Status != WithdrawalStatus.Approved)
            throw new InvalidOperationException("Bạn phải nhận xử lý yêu cầu trước khi xác nhận chuyển khoản.");

        if (preview.Claimedby != actorUserId)
            throw new InvalidOperationException("Yêu cầu đang được một nhân viên khác xử lý.");

        var paidAtUtc = request.PaidAt.Value.UtcDateTime;
        var validationTime = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;
        if (paidAtUtc > validationTime.AddMinutes(5))
            throw new InvalidOperationException("Thời gian chuyển khoản không được nằm trong tương lai.");

        if (preview.Requestedat.HasValue
            && paidAtUtc < preview.Requestedat.Value.AddMinutes(-5))
            throw new InvalidOperationException("Thời gian chuyển khoản không thể trước thời gian tạo yêu cầu.");

        var proofImagePath = await fileStorageService.UploadPrivateFileAsync(
            StorageBucket.PayoutProofs,
            $"withdrawal-{withdrawalId}",
            request.ProofImage);

        Withdrawalrequest withdrawal;
        try
        {
            await using var dbTransaction = await context.Database.BeginTransactionAsync(ct);
            withdrawal = await context.Withdrawalrequests
                .FromSqlInterpolated($"""
                    SELECT *
                    FROM public.withdrawal_requests
                    WHERE withdrawal_id = {withdrawalId}
                    FOR UPDATE
                    """)
                .FirstOrDefaultAsync(ct)
                ?? throw new KeyNotFoundException("Không tìm thấy yêu cầu rút tiền");

            if (withdrawal.Status != WithdrawalStatus.Approved
                || withdrawal.Claimedby != actorUserId)
                throw new InvalidOperationException("Quyền xử lý yêu cầu đã thay đổi. Vui lòng tải lại trang.");

            var transactionExists = await context.PaymentTransactions
                .AsNoTracking()
                .AnyAsync(t => t.Paymentmethod == PaymentTransactionMethod.Manual
                    && t.Providertransactionid != null
                    && t.Providertransactionid.ToUpper() == transactionId, ct);

            if (transactionExists)
                throw new InvalidOperationException($"Mã giao dịch '{transactionId}' đã được ghi nhận trước đó.");

            var decision = string.Equals(actorRole, UserRole.Staff, StringComparison.OrdinalIgnoreCase)
                ? Decisions.StaffApproved
                : Decisions.AdminApproved;

            withdrawal.Status = WithdrawalStatus.Completed;
            withdrawal.Processedby = actorUserId;
            withdrawal.Processedat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;
            withdrawal.Decision = decision;
            withdrawal.Completionnote = note;
            withdrawal.Rejectionreason = null;

            var capture = PaymentTransactionCapture.FromManual(request.PaidAt, actorUserId, note, transactionId);
            var payoutTransaction = capture.Create(
                PaymentTransactionPurpose.Withdrawal,
                PaymentTransactionDirection.Outbound,
                withdrawal.Amount ?? 0,
                withdrawal.Userid,
                null,
                withdrawalId: withdrawal.Withdrawalid,
                description: "Manual withdrawal payout",
                destinationAccountNumber: withdrawal.Accountnumber,
                destinationAccountName: withdrawal.Accountholdername,
                destinationBankName: withdrawal.Bankname);
            payoutTransaction.Proofimagepath = proofImagePath;
            context.PaymentTransactions.Add(payoutTransaction);

            await context.SaveChangesAsync(ct);
            await dbTransaction.CommitAsync(ct);
        }
        catch
        {
            await fileStorageService.DeleteFileAsync(
                StorageBucket.PayoutProofs,
                $"withdrawal-{withdrawalId}",
                proofImagePath);
            throw;
        }

        try
        {
            await notificationService.CreateNotificationAsync(new NotificationRequest
            {
                Userid = withdrawal.Userid!,
                Title = "Yêu cầu rút tiền đã hoàn tất",
                Message = "Yêu cầu rút tiền của bạn đã được duyệt và số tiền đã được chuyển vào tài khoản ngân hàng của bạn."
            });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to send completed withdrawal notification for {WithdrawalId}", withdrawalId);
        }

        logger.LogInformation(
            "{Role} {ActorId} completed withdrawal {WithdrawalId} with transaction {TransactionId}",
            actorRole,
            actorUserId,
            withdrawalId,
            transactionId);

        return new ApproveResult
        {
            Success = true,
            Message = "Đã duyệt và xác nhận chuyển tiền thành công",
            TransactionId = transactionId
        };
    }

    public async Task<RejectResult> RejectRequestAsync(int withdrawalId, string actorUserId, string reason, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new InvalidOperationException("Vui lòng nhập lý do từ chối.");

        await using var transaction = await context.Database.BeginTransactionAsync(ct);
        try
        {
            var withdrawal = await context.Withdrawalrequests
                .FromSqlInterpolated($"""
                    SELECT *
                    FROM public.withdrawal_requests
                    WHERE withdrawal_id = {withdrawalId}
                    FOR UPDATE
                    """)
                .FirstOrDefaultAsync(ct);

            if (withdrawal == null)
                throw new KeyNotFoundException("Không tìm thấy yêu cầu rút tiền");

            if (withdrawal.Status != WithdrawalStatus.Approved)
                throw new InvalidOperationException("Bạn phải nhận xử lý yêu cầu trước khi từ chối.");

            if (withdrawal.Claimedby != actorUserId)
                throw new InvalidOperationException("Yêu cầu đang được một nhân viên khác xử lý.");

            var tutorId = withdrawal.Userid!;
            var amount = withdrawal.Amount ?? 0;
            var rejectionReason = reason.Trim();

            withdrawal.Status = WithdrawalStatus.Rejected;
            withdrawal.Processedby = actorUserId;
            withdrawal.Processedat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;
            withdrawal.Rejectionreason = rejectionReason;
            withdrawal.Completionnote = null;

            // Refund wallet
            var wallet = await walletRepo.GetOrCreateForUpdateAsync(tutorId, ct);

            wallet.Balance = (wallet.Balance ?? 0) + amount;
            wallet.Lastupdated = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;

            walletRepo.AddTransaction(new Wallettransaction
            {
                Walletid = wallet.Walletid,
                Amount = amount,
                Transactiontype = TransactionType.Refund,
                Referencetable = ReferenceTable.Withdrawal,
                Referenceid = withdrawalId,
                Description = $"Refund for rejected withdrawal #{withdrawalId}: {rejectionReason}",
                Createdat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow
            });

            await walletRepo.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            try
            {
                await notificationService.CreateNotificationAsync(new NotificationRequest
                {
                    Userid = tutorId,
                    Title = "Yêu cầu rút tiền bị từ chối",
                    Message = $"Yêu cầu rút tiền bị từ chối: {rejectionReason}. Tiền đã được hoàn về ví."
                });
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to send rejected withdrawal notification for {WithdrawalId}", withdrawalId);
            }

            logger.LogInformation(
                "Actor {ActorId} rejected withdrawal {WithdrawalId}: {Reason}",
                actorUserId,
                withdrawalId,
                rejectionReason);

            return new RejectResult
            {
                Success = true,
                Message = "Đã từ chối và hoàn tiền thành công"
            };
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
    }
}
