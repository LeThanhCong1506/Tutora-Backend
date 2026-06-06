using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.FraudDetection;
using MV.DomainLayer.Entities;
using MV.DomainLayer.Settings;
using MV.ApplicationLayer.Interfaces;
using System.Text.Json;
using static MV.DomainLayer.Constants.LessonStatus;

namespace MV.ApplicationLayer.Services;

public class FraudDetectionService(
    IAppDbContext context,
    IDistributedCache cache,
    IOptions<FraudDetectionSettings> settings,
    ILogger<FraudDetectionService> logger) : IFraudDetectionService
{
    private readonly FraudDetectionSettings _settings = settings.Value;

    public async Task<FraudCheckResult> RunAllRulesAsync(string tutorId, decimal amount, CancellationToken ct = default)
    {
        var timeout = TimeSpan.FromSeconds(_settings.FraudCheckTimeoutSeconds);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);

        try
        {
            var results = new List<RuleResult>
            {
                await CheckVelocityAsync(tutorId, cts.Token),
                await CheckAmountLimitsAsync(tutorId, amount, cts.Token),
                await CheckNewAccountAsync(tutorId, amount, cts.Token),
                await CheckBankChangeCooldownAsync(tutorId, cts.Token),
                await CheckLessonRequiredAsync(tutorId, cts.Token),
                await CheckPatternsAsync(tutorId, amount, cts.Token)
            };

            return new FraudCheckResult
            {
                RuleResults = results
            };
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("Fraud check timeout for tutor {TutorId}", tutorId);
            throw;
        }
    }

    public async Task LogFraudCheckAsync(string tutorId, int? withdrawalId, FraudCheckResult result, CancellationToken ct = default)
    {
        var logs = result.RuleResults.Select(r => new FraudLog
        {
            Tutorid = tutorId,
            Withdrawalrequestid = withdrawalId,
            Rulename = r.RuleName,
            Passed = r.Passed,
            Isflagged = r.IsFlagged,
            Message = r.Message,
            Metadata = r.Metadata != null ? JsonSerializer.Serialize(r.Metadata) : null,
            Checkedat = MV.DomainLayer.Helpers.VietnamTimeHelper.Now
        }).ToList();

        context.Fraudlogs.AddRange(logs);
        await context.SaveChangesAsync(ct);

        logger.LogInformation("Logged {Count} fraud check results for tutor {TutorId}", logs.Count, tutorId);
    }

    private async Task<RuleResult> CheckVelocityAsync(string tutorId, CancellationToken ct)
    {
        var now = MV.DomainLayer.Helpers.VietnamTimeHelper.Now;
        var today = now.Date;
        var weekStart = today.AddDays(-(int)today.DayOfWeek);
        var monthStart = new DateTime(now.Year, now.Month, 1);

        var withdrawals = await context.Withdrawalrequests
            .AsNoTracking()
            .Where(w => w.Userid == tutorId
                        && w.Requestedat >= monthStart
                        && (w.Status == WithdrawalStatus.Pending
                            || w.Status == WithdrawalStatus.Delayed
                            || w.Status == WithdrawalStatus.Approved))
            .Select(w => w.Requestedat)
            .ToListAsync(ct);

        var dailyCount = withdrawals.Count(d => d.HasValue && d.Value >= today);
        var weeklyCount = withdrawals.Count(d => d >= weekStart);
        var monthlyCount = withdrawals.Count;

        var metadata = new Dictionary<string, object>
        {
            ["daily_count"] = dailyCount,
            ["weekly_count"] = weeklyCount,
            ["monthly_count"] = monthlyCount,
            ["daily_limit"] = _settings.MaxWithdrawalsPerDay,
            ["weekly_limit"] = _settings.MaxWithdrawalsPerWeek,
            ["monthly_limit"] = _settings.MaxWithdrawalsPerMonth
        };

        if (dailyCount >= _settings.MaxWithdrawalsPerDay)
            return RuleResult.Fail(FraudRuleConstants.RuleNames.VelocityCheck,
                FraudRuleConstants.ErrorMessages.VelocityExceededDaily, metadata);

        if (weeklyCount >= _settings.MaxWithdrawalsPerWeek)
            return RuleResult.Fail(FraudRuleConstants.RuleNames.VelocityCheck,
                FraudRuleConstants.ErrorMessages.VelocityExceededWeekly, metadata);

        if (monthlyCount >= _settings.MaxWithdrawalsPerMonth)
            return RuleResult.Fail(FraudRuleConstants.RuleNames.VelocityCheck,
                FraudRuleConstants.ErrorMessages.VelocityExceededMonthly, metadata);

        return RuleResult.Pass(FraudRuleConstants.RuleNames.VelocityCheck, metadata);
    }

    private async Task<RuleResult> CheckAmountLimitsAsync(string tutorId, decimal amount, CancellationToken ct)
    {
        var now = MV.DomainLayer.Helpers.VietnamTimeHelper.Now;
        var today = now.Date;
        var monthStart = new DateTime(now.Year, now.Month, 1);

        if (amount > _settings.MaxAmountPerTransaction)
        {
            return RuleResult.Fail(FraudRuleConstants.RuleNames.AmountLimits,
                FraudRuleConstants.ErrorMessages.AmountLimitPerTransaction,
                new Dictionary<string, object>
                {
                    ["requested_amount"] = amount,
                    ["limit"] = _settings.MaxAmountPerTransaction
                });
        }

        var withdrawals = await context.Withdrawalrequests
            .AsNoTracking()
            .Where(w => w.Userid == tutorId
                        && w.Requestedat >= monthStart
                        && (w.Status == WithdrawalStatus.Pending
                            || w.Status == WithdrawalStatus.Delayed
                            || w.Status == WithdrawalStatus.Approved))
            .Select(w => new { w.Requestedat, w.Amount })
            .ToListAsync(ct);

        var dailyTotal = withdrawals.Where(w => w.Requestedat.HasValue && w.Requestedat.Value >= today).Sum(w => w.Amount ?? 0);
        var monthlyTotal = withdrawals.Sum(w => w.Amount ?? 0);

        var metadata = new Dictionary<string, object>
        {
            ["requested_amount"] = amount,
            ["daily_total"] = dailyTotal,
            ["monthly_total"] = monthlyTotal,
            ["daily_limit"] = _settings.MaxAmountPerDay,
            ["monthly_limit"] = _settings.MaxAmountPerMonth
        };

        if (dailyTotal + amount > _settings.MaxAmountPerDay)
            return RuleResult.Fail(FraudRuleConstants.RuleNames.AmountLimits,
                FraudRuleConstants.ErrorMessages.AmountLimitDaily, metadata);

        if (monthlyTotal + amount > _settings.MaxAmountPerMonth)
            return RuleResult.Fail(FraudRuleConstants.RuleNames.AmountLimits,
                FraudRuleConstants.ErrorMessages.AmountLimitMonthly, metadata);

        return RuleResult.Pass(FraudRuleConstants.RuleNames.AmountLimits, metadata);
    }

    private async Task<RuleResult> CheckNewAccountAsync(string tutorId, decimal amount, CancellationToken ct)
    {
        var tutor = await context.Tutorprofiles
            .AsNoTracking()
            .Where(t => t.Tutorid == tutorId)
            .Select(t => new { t.Createdat })
            .FirstOrDefaultAsync(ct);

        if (tutor == null)
        {
            return RuleResult.Fail(FraudRuleConstants.RuleNames.NewAccountRestriction,
                ApiMessages.TutorProfileNotFound, null);
        }

        var accountAge = MV.DomainLayer.Helpers.VietnamTimeHelper.Now - (tutor.Createdat ?? MV.DomainLayer.Helpers.VietnamTimeHelper.Now);
        var accountAgeDays = (int)accountAge.TotalDays;

        var metadata = new Dictionary<string, object>
        {
            ["account_age_days"] = accountAgeDays,
            ["requested_amount"] = amount,
            ["block_days"] = _settings.NewAccountBlockDays,
            ["restrict_days"] = _settings.NewAccountRestrictDays,
            ["new_account_max_amount"] = _settings.NewAccountMaxAmount
        };

        if (accountAgeDays < _settings.NewAccountBlockDays)
            return RuleResult.Fail(FraudRuleConstants.RuleNames.NewAccountRestriction,
                FraudRuleConstants.ErrorMessages.NewAccountBlocked, metadata);

        if (accountAgeDays < _settings.NewAccountRestrictDays && amount > _settings.NewAccountMaxAmount)
            return RuleResult.Fail(FraudRuleConstants.RuleNames.NewAccountRestriction,
                FraudRuleConstants.ErrorMessages.NewAccountRestricted, metadata);

        return RuleResult.Pass(FraudRuleConstants.RuleNames.NewAccountRestriction, metadata);
    }

    private async Task<RuleResult> CheckBankChangeCooldownAsync(string tutorId, CancellationToken ct)
    {
        var tutor = await context.Tutorprofiles
            .AsNoTracking()
            .Where(t => t.Tutorid == tutorId)
            .Select(t => new { t.Bankchangedat })
            .FirstOrDefaultAsync(ct);

        if (tutor?.Bankchangedat == null)
        {
            return RuleResult.Pass(FraudRuleConstants.RuleNames.BankChangeCooldown,
                new Dictionary<string, object> { ["has_bank_change"] = false });
        }

        var hoursSinceChange = (MV.DomainLayer.Helpers.VietnamTimeHelper.Now - tutor.Bankchangedat.Value).TotalHours;

        if (hoursSinceChange < _settings.BankChangeCooldownHours)
        {
            return RuleResult.Fail(FraudRuleConstants.RuleNames.BankChangeCooldown,
                FraudRuleConstants.ErrorMessages.BankChangeCooldown,
                new Dictionary<string, object>
                {
                    ["hours_since_change"] = hoursSinceChange,
                    ["cooldown_hours"] = _settings.BankChangeCooldownHours,
                    ["last_change"] = tutor.Bankchangedat.Value
                });
        }

        var monthStart = new DateTime(MV.DomainLayer.Helpers.VietnamTimeHelper.Now.Year, MV.DomainLayer.Helpers.VietnamTimeHelper.Now.Month, 1);
        var changeCount = await context.BankChangeLogs
            .AsNoTracking()
            .Where(b => b.Tutorid == tutorId && b.Changedat >= monthStart)
            .CountAsync(ct);

        var metadata = new Dictionary<string, object>
        {
            ["hours_since_change"] = hoursSinceChange,
            ["change_count_this_month"] = changeCount,
            ["max_changes_per_month"] = _settings.MaxBankChangesPerMonth
        };

        if (changeCount > _settings.MaxBankChangesPerMonth)
        {
            return RuleResult.Flag(FraudRuleConstants.RuleNames.BankChangeCooldown,
                FraudRuleConstants.ErrorMessages.BankChangeFrequent, metadata);
        }

        return RuleResult.Pass(FraudRuleConstants.RuleNames.BankChangeCooldown, metadata);
    }

    private async Task<RuleResult> CheckLessonRequiredAsync(string tutorId, CancellationToken ct)
    {
        var completedLessons = await context.Lessons
            .AsNoTracking()
            .Where(l => l.Tutorid == tutorId && l.Status == Completed)
            .CountAsync(ct);

        var metadata = new Dictionary<string, object>
        {
            ["completed_lessons"] = completedLessons,
            ["required_lessons"] = _settings.MinCompletedLessons
        };

        if (completedLessons < _settings.MinCompletedLessons)
            return RuleResult.Fail(FraudRuleConstants.RuleNames.LessonRequired,
                FraudRuleConstants.ErrorMessages.NoCompletedLesson, metadata);

        return RuleResult.Pass(FraudRuleConstants.RuleNames.LessonRequired, metadata);
    }

    private async Task<RuleResult> CheckPatternsAsync(string tutorId, decimal amount, CancellationToken ct)
    {
        var flags = new List<string>();
        var metadata = new Dictionary<string, object> { ["requested_amount"] = amount };

        var wallet = await context.Wallets
            .AsNoTracking()
            .Where(w => w.Userid == tutorId)
            .Select(w => w.Balance)
            .FirstOrDefaultAsync(ct);

        if (wallet.HasValue && wallet.Value > 0)
        {
            var withdrawalPercent = (amount / wallet.Value) * 100;
            metadata["balance"] = wallet.Value;
            metadata["withdrawal_percent"] = withdrawalPercent;

            if (withdrawalPercent > _settings.FullBalanceThresholdPercent)
            {
                flags.Add(FraudRuleConstants.ErrorMessages.PatternFullBalance);
            }
        }

        var lookbackDate = MV.DomainLayer.Helpers.VietnamTimeHelper.Now.AddDays(-_settings.IpPatternLookbackDays);
        var distinctIps = await context.Loginhistories
            .AsNoTracking()
            .Where(l => l.Userid == tutorId && l.Loggedat >= lookbackDate)
            .Select(l => l.Ipaddress)
            .Distinct()
            .CountAsync(ct);

        metadata["distinct_ips"] = distinctIps;
        metadata["ip_threshold"] = _settings.MultipleIpThreshold;
        metadata["lookback_days"] = _settings.IpPatternLookbackDays;

        if (distinctIps > _settings.MultipleIpThreshold)
        {
            flags.Add(FraudRuleConstants.ErrorMessages.PatternMultipleIps);
        }

        var recentWithdrawals = await context.Withdrawalrequests
            .AsNoTracking()
            .Where(w => w.Userid == tutorId
                        && w.Status == WithdrawalStatus.Approved
                        && w.Amount.HasValue)
            .OrderByDescending(w => w.Requestedat)
            .Take(10)
            .Select(w => w.Amount!.Value)
            .ToListAsync(ct);

        if (recentWithdrawals.Count >= _settings.MinWithdrawalsForAverage)
        {
            var avgAmount = recentWithdrawals.Average();
            metadata["average_amount"] = avgAmount;
            metadata["unusual_multiplier"] = _settings.UnusualAmountMultiplier;

            if (amount > avgAmount * _settings.UnusualAmountMultiplier)
            {
                flags.Add(FraudRuleConstants.ErrorMessages.PatternUnusualAmount);
            }
        }

        metadata["flags"] = flags;

        if (flags.Any())
        {
            return RuleResult.Flag(FraudRuleConstants.RuleNames.PatternDetection,
                string.Join(" ", flags), metadata);
        }

        return RuleResult.Pass(FraudRuleConstants.RuleNames.PatternDetection, metadata);
    }
}
