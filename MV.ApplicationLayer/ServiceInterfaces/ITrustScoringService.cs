using MV.DomainLayer.DTO.FraudDetection;
using MV.DomainLayer.DTO.TrustScoring;

namespace MV.ApplicationLayer.ServiceInterfaces;

public interface ITrustScoringService
{
    /// <summary>
    /// Calculate trust score (0-100) based on tutor profile, withdrawal amount, and fraud check results
    /// </summary>
    Task<TrustScoreResult> CalculateTrustScoreAsync(
        string tutorId,
        decimal amount,
        FraudCheckResult fraudCheckResult,
        CancellationToken ct = default);

    /// <summary>
    /// Determine approval decision based on trust score and fraud flags
    /// </summary>
    ApprovalDecision GetApprovalDecision(int score, FraudCheckResult fraudCheckResult);

    /// <summary>
    /// Save trust score and decision to withdrawal_scores table
    /// </summary>
    Task SaveScoreAsync(
        int withdrawalId,
        string tutorId,
        TrustScoreResult scoreResult,
        ApprovalDecision decision,
        CancellationToken ct = default);
}
