using MV.DomainLayer.DTO.ResponseModel;

namespace MV.ApplicationLayer.ServiceInterfaces;

/// <summary>
/// Theo dõi trạng thái online/offline của người dùng qua vòng đời kết nối SignalR.
/// Mỗi kết nối canonical giữ một lease riêng trong Redis, vì vậy nhiều tab và nhiều
/// instance không làm sai bộ đếm khi reconnect/disconnect xen kẽ.
/// </summary>
public interface IPresenceService
{
    /// <summary>
    /// Đăng ký (hoặc gia hạn) lease cho một connection. Nếu user vừa chuyển
    /// offline → online thì service phát <c>presenceChanged</c>.
    /// </summary>
    Task RegisterConnectionAsync(string userId, string connectionId);

    /// <summary>Gia hạn lease của connection đang sống.</summary>
    Task RefreshConnectionAsync(string userId, string connectionId);

    /// <summary>
    /// Gỡ chính xác một connection. Nếu đây là lease cuối cùng thì ghi last-seen
    /// và phát <c>presenceChanged</c> offline.
    /// </summary>
    Task RemoveConnectionAsync(string userId, string connectionId);

    /// <summary>Trạng thái hiện tại: online hay không, và nếu offline thì last-seen là khi nào.</summary>
    Task<UserPresenceResponse> GetPresenceAsync(string userId);

    /// <summary>
    /// Đọc presence của nhiều user bằng một Redis round-trip. Mặc định không đọc
    /// last-seen từ PostgreSQL để tránh N+1; caller chỉ nên bật khi danh sách nhỏ.
    /// </summary>
    Task<IReadOnlyList<UserPresenceResponse>> GetPresencesAsync(
        IReadOnlyCollection<string> userIds,
        bool includeLastSeen = false);

    /// <summary>
    /// Thu hồi các lease hết hạn. Được gọi định kỳ bởi hosted service; trả về số
    /// user thực sự chuyển online → offline.
    /// </summary>
    Task<int> CleanupExpiredLeasesAsync(CancellationToken cancellationToken = default);
}
