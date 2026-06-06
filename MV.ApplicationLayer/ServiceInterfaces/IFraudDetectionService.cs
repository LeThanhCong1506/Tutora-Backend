using MV.DomainLayer.DTO.FraudDetection;

namespace MV.ApplicationLayer.ServiceInterfaces;

public interface IFraudDetectionService
{
    /// <summary>
    /// Run all fraud detection rules for a tutor withdrawal and return an aggregate result.
    /// Called before allowing a withdrawal to proceed.
    /// </summary>
    Task<FraudCheckResult> RunAllRulesAsync(string tutorId, decimal amount, CancellationToken ct = default);

    /// <summary>
    /// Persist the fraud check result for audit and future model training.
    /// </summary>
    Task LogFraudCheckAsync(string tutorId, int? withdrawalId, FraudCheckResult result, CancellationToken ct = default);
}
