using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.FraudDetection;
using MV.DomainLayer.DTO.TrustScoring;
using MV.DomainLayer.Entities;
using MV.DomainLayer.Settings;
using MV.ApplicationLayer.Interfaces;
using System.Text.Json;
using static MV.DomainLayer.Constants.LessonStatus;

namespace MV.ApplicationLayer.Services;

public class TrustScoringService(
    IAppDbContext context,
    IOptions<TrustScoringSettings> settings,
    ILogger<TrustScoringService> logger) : ITrustScoringService
{
    private readonly TrustScoringSettings _settings = settings.Value;

    public async Task<TrustScoreResult> CalculateTrustScoreAsync(
        string tutorId,
        decimal amount,
        FraudCheckResult fraudCheckResult,
        CancellationToken ct = default)
    {
        var tutorStats = await context.Tutorprofiles
            .AsNoTracking()
            .Where(t => t.Tutorid == tutorId)
            .Select(t => new
            {
                t.Createdat,
                t.Isbankverified,
                t.Bankchangedat,
                CompletedLessonsCount = context.Lessons
                    .Count(l => l.Tutorid == tutorId && l.Status == Completed),
                DisputesCount = context.Disputes
                    .Count(d => d.Lessonid != null && d.Lesson!.Tutorid == tutorId),
                HasSuccessfulWithdrawal = context.Withdrawalrequests
                    .Any(w => w.Userid == tutorId && w.Status == WithdrawalStatus.Approved)
            })
            .FirstOrDefaultAsync(ct);

        if (tutorStats == null)
        {
            logger.LogWarning("Tutor profile not found for {TutorId}", tutorId);
            return new TrustScoreResult { BaseScore = _settings.BaseScore, TotalScore = 0 };
        }

        var accountAgeDays = (int)(MV.DomainLayer.Helpers.VietnamTimeHelper.Now - (tutorStats.Createdat ?? MV.DomainLayer.Helpers.VietnamTimeHelper.Now)).TotalDays;

        var result = new TrustScoreResult { BaseScore = _settings.BaseScore };

        CalculatePositiveFactors(
            result,
            accountAgeDays,
            tutorStats.CompletedLessonsCount,
            tutorStats.Isbankverified ?? false,
            tutorStats.DisputesCount,
            amount,
            tutorStats.HasSuccessfulWithdrawal);

        CalculateNegativeFactors(
            result,
            accountAgeDays,
            tutorStats.Bankchangedat,
            fraudCheckResult,
            amount,
            tutorStats.HasSuccessfulWithdrawal);

        result.FraudFlags = fraudCheckResult.FlaggedRules.ToList();
        result.TotalScore = Math.Clamp(
            result.BaseScore + result.TotalPositivePoints + result.TotalNegativePoints,
            0,
            100);

        logger.LogInformation(
            "Trust score calculated for tutor {TutorId}: {Score} (Base: {Base}, +{Positive}, {Negative})",
            tutorId, result.TotalScore, result.BaseScore, result.TotalPositivePoints, result.TotalNegativePoints);

        return result;
    }

    public ApprovalDecision GetApprovalDecision(int score, FraudCheckResult fraudCheckResult)
    {
        if (!fraudCheckResult.AllPassed)
        {
            return new ApprovalDecision
            {
                Decision = TrustScoringConstants.Decisions.AutoReject,
                Message = fraudCheckResult.BlockingMessage ?? TrustScoringConstants.Messages.AutoRejectMessage,
                EstimatedProcessingHours = 0,
                RequiresAdmin = false,
                Reason = $"Fraud check failed: {string.Join(", ", fraudCheckResult.BlockingRules)}"
            };
        }

        if (score >= _settings.AutoApproveThreshold)
            return new ApprovalDecision
            {
                Decision = TrustScoringConstants.Decisions.AutoApprove,
                Message = TrustScoringConstants.Messages.AutoApproveMessage,
                EstimatedProcessingHours = 0,
                RequiresAdmin = false
            };

        if (score >= _settings.ManualReviewThreshold)
            return new ApprovalDecision
            {
                Decision = TrustScoringConstants.Decisions.ManualReview,
                Message = TrustScoringConstants.Messages.ManualReviewMessage,
                EstimatedProcessingHours = _settings.ManualReviewProcessingHours,
                RequiresAdmin = true
            };

        return new ApprovalDecision
        {
            Decision = TrustScoringConstants.Decisions.AutoReject,
            Message = TrustScoringConstants.Messages.AutoRejectMessage,
            EstimatedProcessingHours = 0,
            RequiresAdmin = false,
            Reason = $"Trust score too low: {score}"
        };
    }

    public async Task SaveScoreAsync(
        int withdrawalId,
        string tutorId,
        TrustScoreResult scoreResult,
        ApprovalDecision decision,
        CancellationToken ct = default)
    {
        var scoreEntity = new WithdrawalScore
        {
            Withdrawalrequestid = withdrawalId,
            Tutorid = tutorId,
            Basescore = scoreResult.BaseScore,
            Positivefactors = JsonSerializer.Serialize(scoreResult.PositiveFactors),
            Negativefactors = JsonSerializer.Serialize(scoreResult.NegativeFactors),
            Fraudflags = JsonSerializer.Serialize(scoreResult.FraudFlags),
            Totalscore = scoreResult.TotalScore,
            Decision = decision.Decision,
            Createdat = MV.DomainLayer.Helpers.VietnamTimeHelper.Now
        };

        context.Withdrawalscores.Add(scoreEntity);
        await context.SaveChangesAsync(ct);

        logger.LogInformation(
            "Saved trust score for withdrawal {WithdrawalId}: Score={Score}, Decision={Decision}",
            withdrawalId, scoreResult.TotalScore, decision.Decision);
    }

    private void CalculatePositiveFactors(
        TrustScoreResult result,
        int accountAgeDays,
        int completedLessonsCount,
        bool isBankVerified,
        int disputesCount,
        decimal amount,
        bool hasSuccessfulWithdrawal)
    {
        if (accountAgeDays >= _settings.AccountAge90DaysThreshold)
        {
            result.PositiveFactors.Add(new ScoreFactor
            {
                Factor = TrustScoringConstants.PositiveFactors.AccountAge90Plus,
                Points = _settings.AccountAge90PlusPoints,
                Detail = $"Account age {accountAgeDays} days (≥90 days)"
            });
        }
        else if (accountAgeDays >= _settings.AccountAge30DaysThreshold)
        {
            result.PositiveFactors.Add(new ScoreFactor
            {
                Factor = TrustScoringConstants.PositiveFactors.AccountAge30To90,
                Points = _settings.AccountAge30To90Points,
                Detail = $"Account age {accountAgeDays} days (30-90 days)"
            });
        }

        if (completedLessonsCount >= _settings.CompletedLessons10Threshold)
        {
            result.PositiveFactors.Add(new ScoreFactor
            {
                Factor = TrustScoringConstants.PositiveFactors.CompletedLessons10Plus,
                Points = _settings.CompletedLessons10PlusPoints,
                Detail = $"{completedLessonsCount} completed lessons (≥10)"
            });
        }
        else if (completedLessonsCount >= _settings.CompletedLessons5Threshold)
        {
            result.PositiveFactors.Add(new ScoreFactor
            {
                Factor = TrustScoringConstants.PositiveFactors.CompletedLessons5To10,
                Points = _settings.CompletedLessons5To10Points,
                Detail = $"{completedLessonsCount} completed lessons (5-10)"
            });
        }

        if (isBankVerified)
        {
            result.PositiveFactors.Add(new ScoreFactor
            {
                Factor = TrustScoringConstants.PositiveFactors.BankVerified,
                Points = _settings.BankVerifiedPoints,
                Detail = "Bank account verified"
            });
        }

        if (disputesCount == 0)
        {
            result.PositiveFactors.Add(new ScoreFactor
            {
                Factor = TrustScoringConstants.PositiveFactors.NoDisputes,
                Points = _settings.NoDisputesPoints,
                Detail = "No disputes"
            });
        }

        if (amount < _settings.SmallAmountThreshold)
        {
            result.PositiveFactors.Add(new ScoreFactor
            {
                Factor = TrustScoringConstants.PositiveFactors.SmallAmount,
                Points = _settings.SmallAmountPoints,
                Detail = $"Small amount ({amount:N0} VND < {_settings.SmallAmountThreshold:N0} VND)"
            });
        }

        if (hasSuccessfulWithdrawal)
        {
            result.PositiveFactors.Add(new ScoreFactor
            {
                Factor = TrustScoringConstants.PositiveFactors.HasSuccessfulWithdrawal,
                Points = _settings.HasSuccessfulWithdrawalPoints,
                Detail = "Has successful withdrawal history"
            });
        }
    }

    private void CalculateNegativeFactors(
        TrustScoreResult result,
        int accountAgeDays,
        DateTime? bankChangedAt,
        FraudCheckResult fraudCheckResult,
        decimal amount,
        bool hasSuccessfulWithdrawal)
    {
        if (accountAgeDays < _settings.AccountAge30DaysThreshold)
        {
            result.NegativeFactors.Add(new ScoreFactor
            {
                Factor = TrustScoringConstants.NegativeFactors.NewAccount,
                Points = -_settings.NewAccountPenalty,
                Detail = $"New account ({accountAgeDays} days < 30 days)"
            });
        }

        if (bankChangedAt.HasValue)
        {
            var daysSinceChange = (MV.DomainLayer.Helpers.VietnamTimeHelper.Now - bankChangedAt.Value).TotalDays;
            if (daysSinceChange < _settings.BankChangedRecentlyDays)
            {
                result.NegativeFactors.Add(new ScoreFactor
                {
                    Factor = TrustScoringConstants.NegativeFactors.BankChangedRecently,
                    Points = -_settings.BankChangedRecentlyPenalty,
                    Detail = $"Bank changed recently ({daysSinceChange:N0} days < {_settings.BankChangedRecentlyDays} days)"
                });
            }
        }

        var patternRule = fraudCheckResult.RuleResults
            .FirstOrDefault(r => r.RuleName == FraudRuleConstants.RuleNames.PatternDetection);

        if (patternRule?.Metadata?.TryGetValue("distinct_ips", out var ipsValue) == true &&
            ipsValue is JsonElement ipsElement &&
            ipsElement.TryGetInt32(out int distinctIps) &&
            distinctIps > 3)
        {
            result.NegativeFactors.Add(new ScoreFactor
            {
                Factor = TrustScoringConstants.NegativeFactors.MultipleIps,
                Points = -_settings.MultipleIpsPenalty,
                Detail = $"Multiple different IPs ({distinctIps} IPs)"
            });
        }

        if (!hasSuccessfulWithdrawal)
        {
            result.NegativeFactors.Add(new ScoreFactor
            {
                Factor = TrustScoringConstants.NegativeFactors.FirstWithdrawal,
                Points = -_settings.FirstWithdrawalPenalty,
                Detail = "First withdrawal"
            });
        }

        if (fraudCheckResult.FlaggedRules.Any(f => f.Contains("rút toàn bộ số dư", StringComparison.OrdinalIgnoreCase) ||
                                                     f.Contains("full balance", StringComparison.OrdinalIgnoreCase)))
        {
            result.NegativeFactors.Add(new ScoreFactor
            {
                Factor = TrustScoringConstants.NegativeFactors.FullBalanceWithdrawal,
                Points = -_settings.FullBalanceWithdrawalPenalty,
                Detail = "Withdrawing nearly full balance (>95%)"
            });
        }

        if (amount > _settings.LargeAmountThreshold)
        {
            result.NegativeFactors.Add(new ScoreFactor
            {
                Factor = TrustScoringConstants.NegativeFactors.LargeAmount,
                Points = -_settings.LargeAmountPenalty,
                Detail = $"Large amount ({amount:N0} VND > {_settings.LargeAmountThreshold:N0} VND)"
            });
        }
    }
}
