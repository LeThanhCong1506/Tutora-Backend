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
    ILogger<AdminPayoutService> logger) : IAdminPayoutService
{
    private static readonly string[] StaffActionableStatuses =
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
                // Approved is a legacy in-flight state from the old automated-payout flow and no
                // longer means "paid out"; AdminFinancialService/AdminDashboardService already
                // bucket it under "outstanding", so keep this consistent: only Completed counts.
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
            .Where(w => StaffActionableStatuses.Contains(w.Status ?? ""));

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
                w.Amount,
                w.Requestedat
            })
            .ToListAsync(ct);

        var result = items.Select(i => new PendingReviewItem
        {
            WithdrawalId = i.Withdrawalid,
            TutorId = i.Userid ?? "",
            TutorName = i.TutorName ?? "",
            Amount = i.Amount ?? 0,
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

        if (!string.IsNullOrEmpty(status))
            query = query.Where(w => w.Status == status);
        if (!string.IsNullOrEmpty(search))
            query = query.Where(w => w.Userid!.Contains(search) || w.Accountholdername!.Contains(search));
        if (from.HasValue)
            query = query.Where(w => w.Requestedat >= from.Value);
        if (to.HasValue)
            query = query.Where(w => w.Requestedat <= to.Value);

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
                CompletionNote = withdrawal.Completionnote
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

        if (withdrawal.Processedat.HasValue)
            timeline.Add(new() { Timestamp = withdrawal.Processedat.Value, Event = "Processed", Details = $"Status: {withdrawal.Status}. Ghi chú: {withdrawal.Completionnote}" });

        return timeline;
    }

    public async Task<ApproveResult> ApproveRequestAsync(
        int withdrawalId,
        string actorUserId,
        string actorRole,
        ApproveWithdrawalRequest request,
        CancellationToken ct = default)
    {
        await using var dbTransaction =
            await context.Database.BeginTransactionAsync(
                System.Data.IsolationLevel.Serializable,
                ct);
        var withdrawal = await withdrawalRepo.GetByIdWithUserAsync(withdrawalId, ct);

        if (withdrawal == null)
            throw new KeyNotFoundException("Không tìm thấy yêu cầu rút tiền");

        if (!StaffActionableStatuses.Contains(withdrawal.Status ?? ""))
            throw new InvalidOperationException($"Không thể duyệt yêu cầu có trạng thái: {withdrawal.Status}");

        if (request == null
            || string.IsNullOrWhiteSpace(request.TransactionId)
            || !request.PaidAt.HasValue
            || string.IsNullOrWhiteSpace(request.Note))
            throw new InvalidOperationException("Vui lòng nhập đầy đủ mã giao dịch, thời gian chuyển khoản và ghi chú đối soát.");

        var transactionId = request.TransactionId.Trim().ToUpperInvariant();
        var note = request.Note.Trim();

        var transactionExists = await context.PaymentTransactions
            .AsNoTracking()
            .AnyAsync(t => t.Paymentmethod == PaymentTransactionMethod.Manual
                && t.Providertransactionid != null
                && t.Providertransactionid.ToUpper() == transactionId, ct);

        if (transactionExists)
            throw new InvalidOperationException($"Mã giao dịch '{transactionId}' đã được ghi nhận trước đó.");

        // Ghi đúng decision theo role người thực hiện
        var decision = string.Equals(actorRole, UserRole.Staff, StringComparison.OrdinalIgnoreCase)
            ? Decisions.StaffApproved
            : Decisions.AdminApproved;

        // Staff đã tự chuyển tiền thủ công trước khi bấm nút này; 1 bước, không còn trạng thái
        // Approved trung gian chờ PayOS xử lý async như trước.
        withdrawal.Status         = WithdrawalStatus.Completed;
        withdrawal.Processedby    = actorUserId;
        withdrawal.Processedat    = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;
        withdrawal.Decision       = decision;
        withdrawal.Completionnote = note;

        var capture = PaymentTransactionCapture.FromManual(request.PaidAt, actorUserId, note, transactionId);
        context.PaymentTransactions.Add(capture.Create(
            PaymentTransactionPurpose.Withdrawal,
            PaymentTransactionDirection.Outbound,
            withdrawal.Amount ?? 0,
            withdrawal.Userid,
            null,
            withdrawalId: withdrawal.Withdrawalid,
            description: "Manual withdrawal payout",
            destinationAccountNumber: withdrawal.Accountnumber,
            destinationAccountName: withdrawal.Accountholdername,
            destinationBankName: withdrawal.Bankname));

        await context.SaveChangesAsync(ct);
        await dbTransaction.CommitAsync(ct);

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

        logger.LogInformation("{Role} {ActorId} approved & completed withdrawal {WithdrawalId} (decision={Decision})",
            actorRole, actorUserId, withdrawalId, decision);

        return new ApproveResult
        {
            Success = true,
            Message = "Đã duyệt và xác nhận chuyển tiền thành công"
        };
    }

    public async Task<RejectResult> RejectRequestAsync(int withdrawalId, string actorUserId, string reason, CancellationToken ct = default)
    {
        using var transaction = await context.Database.BeginTransactionAsync(ct);
        try
        {
            var withdrawal = await withdrawalRepo.GetByIdWithUserAsync(withdrawalId, ct);

            if (withdrawal == null)
                throw new KeyNotFoundException("Không tìm thấy yêu cầu rút tiền");

            if (!StaffActionableStatuses.Contains(withdrawal.Status ?? ""))
                throw new InvalidOperationException($"Không thể từ chối yêu cầu có trạng thái: {withdrawal.Status}");

            var tutorId = withdrawal.Userid!;
            var amount = withdrawal.Amount ?? 0;

            withdrawal.Status     = WithdrawalStatus.Rejected;
            withdrawal.Processedby = actorUserId;
            withdrawal.Processedat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;

            // Refund wallet
            var wallet = await walletRepo.GetByUserIdAsync(tutorId, ct);
            if (wallet == null)
            {
                wallet = new Wallet
                {
                    Userid = tutorId,
                    Balance = 0,
                    Frozenbalance = 0,
                    Lastupdated = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow
                };
                walletRepo.Add(wallet);
                await walletRepo.SaveChangesAsync(ct);
            }

            wallet.Balance = (wallet.Balance ?? 0) + amount;
            wallet.Lastupdated = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;

            walletRepo.AddTransaction(new Wallettransaction
            {
                Walletid = wallet.Walletid,
                Amount = amount,
                Transactiontype = TransactionType.Refund,
                Referencetable = ReferenceTable.Withdrawal,
                Referenceid = withdrawalId,
                Description = $"Refund for rejected withdrawal #{withdrawalId}: {reason}",
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
                    Message = $"Yêu cầu rút tiền bị từ chối: {reason}. Tiền đã được hoàn về ví."
                });
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to send rejected withdrawal notification for {WithdrawalId}", withdrawalId);
            }

            logger.LogInformation("Actor {ActorId} rejected withdrawal {WithdrawalId}: {Reason}", actorUserId, withdrawalId, reason);

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
