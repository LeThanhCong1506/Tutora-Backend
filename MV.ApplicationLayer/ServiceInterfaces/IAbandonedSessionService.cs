namespace MV.ApplicationLayer.ServiceInterfaces;

/// <summary>
/// Xử lý buổi học trôi qua giờ mà không ai vào lớp. Không có job nào trước đây động tới chúng nên
/// buổi nằm lại ở <c>scheduled</c> vĩnh viễn, kéo theo <c>Sessionsremaining</c> không bao giờ về 0
/// — nghĩa là escrow của TẤT CẢ các buổi gia sư đã dạy thật trong cùng booking cũng bị treo theo.
/// </summary>
public interface IAbandonedSessionService
{
    /// <summary>
    /// Quét các buổi quá hạn và xử lý theo mức độ có mặt của hai bên. Trả về số buổi đã đụng tới.
    /// </summary>
    Task<int> ProcessAbandonedSessionsAsync(CancellationToken ct = default);
}
