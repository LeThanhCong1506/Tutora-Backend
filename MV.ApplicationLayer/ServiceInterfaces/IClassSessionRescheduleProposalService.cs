using MV.DomainLayer.DTO.ResponseModel;

namespace MV.ApplicationLayer.ServiceInterfaces;

public interface IClassSessionRescheduleProposalService
{
    Task<ClassSessionRescheduleProposalResponse> ProposeAsync(
        int classSessionId,
        string userId,
        string? role,
        DateTime proposedScheduledStart,
        string? reason,
        CancellationToken cancellationToken = default);

    Task<ClassSessionRescheduleProposalResponse> RespondAsync(
        int classSessionId,
        string userId,
        string? role,
        bool accepted,
        CancellationToken cancellationToken = default);

    /// <summary>Toàn bộ lịch sử đề xuất đổi lịch của buổi học, mới nhất trước. Không tự kiểm quyền —
    /// chỉ gọi từ trong luồng lấy chi tiết buổi học đã được xác thực quyền từ trước.</summary>
    Task<List<ClassSessionRescheduleProposalResponse>> GetProposalHistoryAsync(
        int classSessionId,
        CancellationToken cancellationToken = default);

    /// <summary>Quét các đề xuất đang chờ đã quá hạn, chuyển sang Expired và báo người đề xuất.</summary>
    Task<int> ExpireOverdueProposalsAsync(CancellationToken cancellationToken = default);
}
