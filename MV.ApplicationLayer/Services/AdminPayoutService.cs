using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MV.ApplicationLayer.JobHandlers;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.DTO.ResponseModel.Admin;
using MV.DomainLayer.Entities;
using MV.DomainLayer.Helpers;
using MV.ApplicationLayer.Interfaces;
using MV.ApplicationLayer.RepositoryInterfaces;
using System.Text.Json;
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
    public async Task<PayoutOverviewResponse> GetOverviewAsync(CancellationToken ct = default)
    {
        var today = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow.Date;
        var thisMonth = new DateTime(MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow.Year, MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow.Month, 1);

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
                Completed = g.Count(w => w.Status == WithdrawalStatus.Approved || w.Status == WithdrawalStatus.Completed),
                TotalPayoutThisMonth = g.Where(w => w.Status == WithdrawalStatus.Approved || w.Status == WithdrawalStatus.Completed).Sum(w => w.Amount ?? 0)
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
                TotalRequests = monthStats?.TotalRequests ?? 0,
                AutoApproved = monthStats?.AutoApproved ?? 0,
                Delayed = monthStats?.Delayed ?? 0,
                ManualReview = monthStats?.ManualReview ?? 0,
                Rejected = monthStats?.Rejected ?? 0
            },
            ProcessingStats = new ProcessingStatsResponse
            {
                AvgProcessingTime = 0,
                SuccessRate = successRate,
                PendingCount = pendingCount
            },
            FinancialStats = new FinancialStatsResponse
            {
                TotalPayoutToday = monthStats?.TotalPayoutThisMonth ?? 0,
                TotalPayoutThisMonth = monthStats?.TotalPayoutThisMonth ?? 0
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
            .Where(w => w.Decision == Decisions.ManualReview && w.Status == WithdrawalStatus.PendingReview);

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

        var withdrawalIds = items.Select(i => i.Withdrawalid).ToList();

        var scores = await context.Withdrawalscores
            .AsNoTracking()
            .Where(s => withdrawalIds.Contains(s.Withdrawalrequestid))
            .ToDictionaryAsync(s => s.Withdrawalrequestid, s => s.Totalscore, ct);

        var fraudFlags = await context.Fraudlogs
            .AsNoTracking()
            .Where(f => withdrawalIds.Contains(f.Withdrawalrequestid!.Value) && f.Isflagged)
            .GroupBy(f => f.Withdrawalrequestid!.Value)
            .Select(g => new { WithdrawalId = g.Key, Flags = g.Select(f => f.Rulename).Take(3).ToList() })
            .ToDictionaryAsync(x => x.WithdrawalId, x => x.Flags, ct);

        var result = items.Select(i => new PendingReviewItem
        {
            WithdrawalId = i.Withdrawalid,
            TutorId = i.Userid ?? "",
            TutorName = i.TutorName ?? "",
            Amount = i.Amount ?? 0,
            TrustScore = scores.GetValueOrDefault(i.Withdrawalid),
            TopFraudFlags = fraudFlags.GetValueOrDefault(i.Withdrawalid, []),
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

        // Complex multi-entity analytics: stays on context
        var score = await context.Withdrawalscores.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Withdrawalrequestid == withdrawalId, ct);

        var fraudFlags = await context.Fraudlogs.AsNoTracking()
            .Where(f => f.Withdrawalrequestid == withdrawalId && f.Isflagged)
            .Select(f => f.Message ?? f.Rulename)
            .ToListAsync(ct);

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

        var scoreBreakdown = score is null ? null : new ScoreBreakdownResponse
        {
            BaseScore = score.Basescore,
            PositiveFactors = ParseScoreFactors(score.Positivefactors),
            NegativeFactors = ParseScoreFactors(score.Negativefactors),
            TotalScore = score.Totalscore,
            Decision = score.Decision
        };

        var walletInfo = new WalletInfoResponse
        {
            Balance = wallet?.Balance ?? 0,
            FrozenBalance = wallet?.Frozenbalance ?? 0,
            AvailableBalance = (wallet?.Balance ?? 0) - (wallet?.Frozenbalance ?? 0)
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
                PayosTransactionId = withdrawal.Payostransactionid,
                PayosStatus = withdrawal.Payosstatus
            },
            TutorInfo = tutorInfo,
            ScoreBreakdown = scoreBreakdown,
            FraudFlags = fraudFlags,
            PreviousWithdrawals = previousWithdrawals,
            WalletInfo = walletInfo,
            Timeline = timeline.OrderBy(t => t.Timestamp).ToList()
        };
    }

    private static List<string> ParseScoreFactors(string? json)
    {
        if (string.IsNullOrEmpty(json)) return [];
        try
        {
            var strings = JsonSerializer.Deserialize<List<string>>(json);
            if (strings != null) return strings;
        }
        catch
        {
            try
            {
                var elements = JsonSerializer.Deserialize<List<JsonElement>>(json);
                if (elements != null)
                    return elements.Select(e =>
                        e.TryGetProperty("Detail", out var detail) ? detail.GetString() ?? "" :
                        e.TryGetProperty("detail", out var detail2) ? detail2.GetString() ?? "" :
                        e.ToString()
                    ).ToList();
            }
            catch { }
        }
        return [];
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
            timeline.Add(new() { Timestamp = withdrawal.Processedat.Value, Event = "Processed", Details = $"Status: {withdrawal.Status}" });

        return timeline;
    }

    public async Task<ApproveResult> ApproveRequestAsync(int withdrawalId, string actorUserId, string actorRole, string? note = null, CancellationToken ct = default)
    {
        var withdrawal = await withdrawalRepo.GetByIdWithUserAsync(withdrawalId, ct);

        if (withdrawal == null)
            throw new KeyNotFoundException("Không tìm thấy yêu cầu rút tiền");

        if (withdrawal.Status != WithdrawalStatus.PendingReview && withdrawal.Status != WithdrawalStatus.Delayed)
            throw new InvalidOperationException($"Không thể duyệt yêu cầu có trạng thái: {withdrawal.Status}");

        // Ghi đúng decision theo role người thực hiện
        var decision = string.Equals(actorRole, UserRole.Staff, StringComparison.OrdinalIgnoreCase)
            ? Decisions.StaffApproved
            : Decisions.AdminApproved;

        withdrawal.Status     = WithdrawalStatus.Approved;
        withdrawal.Processedby = actorUserId;
        withdrawal.Processedat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;
        withdrawal.Decision    = decision;

        await withdrawalRepo.SaveChangesAsync(ct);

        BackgroundJob.Enqueue<PayoutJobHandler>(x => x.ProcessImmediatePayoutAsync(withdrawalId, CancellationToken.None));

        await notificationService.CreateNotificationAsync(new NotificationRequest
        {
            Userid = withdrawal.Userid!,
            Title = "Yêu cầu rút tiền đã được phê duyệt",
            Message = "Yêu cầu rút tiền của bạn đã được phê duyệt. Đang xử lý, dự kiến 5-30 phút."
        });

        logger.LogInformation("{Role} {ActorId} approved withdrawal {WithdrawalId} (decision={Decision})",
            actorRole, actorUserId, withdrawalId, decision);

        return new ApproveResult
        {
            Success = true,
            Message = "Đã phê duyệt yêu cầu rút tiền thành công",
            EstimatedTime = "5-30 phút"
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

            if (withdrawal.Status != WithdrawalStatus.PendingReview && withdrawal.Status != WithdrawalStatus.Delayed)
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

            await notificationService.CreateNotificationAsync(new NotificationRequest
            {
                Userid = tutorId,
                Title = "Yêu cầu rút tiền bị từ chối",
                Message = $"Yêu cầu rút tiền bị từ chối: {reason}. Tiền đã được hoàn về ví."
            });

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

    public async Task<FraudLogResponse> GetFraudLogsAsync(
        int page,
        int pageSize,
        string? tutorId = null,
        string? ruleName = null,
        bool? passed = null,
        DateTime? from = null,
        DateTime? to = null,
        CancellationToken ct = default)
    {
        // Complex multi-join query: stays on context
        IQueryable<FraudLog> query = context.Fraudlogs
            .AsNoTracking()
            .Include(f => f.Tutor);

        if (!string.IsNullOrEmpty(tutorId))
            query = query.Where(f => f.Tutorid == tutorId);
        if (!string.IsNullOrEmpty(ruleName))
            query = query.Where(f => f.Rulename == ruleName);
        if (passed.HasValue)
            query = query.Where(f => f.Passed == passed.Value);
        if (from.HasValue)
            query = query.Where(f => f.Checkedat >= from.Value);
        if (to.HasValue)
            query = query.Where(f => f.Checkedat <= to.Value);

        var total = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(f => f.Checkedat)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var tutorIds = items.Select(f => f.Tutorid).Distinct().ToList();
        var tutorNames = await context.Users
            .AsNoTracking()
            .Where(u => tutorIds.Contains(u.Userid))
            .Select(u => new { u.Userid, Name = u.Fullname ?? u.Username ?? "" })
            .ToDictionaryAsync(u => u.Userid, u => u.Name, ct);

        var result = items.Select(f => new FraudLogItem
        {
            LogId = f.Logid,
            TutorId = f.Tutorid,
            TutorName = tutorNames.GetValueOrDefault(f.Tutorid, ""),
            WithdrawalRequestId = f.Withdrawalrequestid,
            RuleName = f.Rulename,
            Passed = f.Passed,
            IsFlagged = f.Isflagged,
            Message = f.Message,
            CheckedAt = f.Checkedat
        }).ToList();

        return new FraudLogResponse
        {
            Items = result,
            Total = total,
            Page = page,
            PageSize = pageSize
        };
    }
}
