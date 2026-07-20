using System.Threading;
using System.Threading.Tasks;

namespace MV.ApplicationLayer.ServiceInterfaces;

/// <summary>Thông tin để client join phòng Agora Interactive Whiteboard (Netless).</summary>
/// <param name="AppIdentifier">App Identifier để khởi tạo Whiteboard SDK ở client.</param>
/// <param name="Region">Region của phòng (vd "sg").</param>
/// <param name="RoomUuid">UUID phòng whiteboard.</param>
/// <param name="RoomToken">Room token của người dùng (đã gắn role).</param>
/// <param name="Role">Vai trò: "0" admin (tutor), "1" writer (học viên), "2" reader.</param>
public record WhiteboardRoomInfo(
    string AppIdentifier,
    string Region,
    string RoomUuid,
    string RoomToken,
    string Role);

/// <summary>
/// Quản lý phòng Agora Interactive Whiteboard (Netless) cho buổi học.
/// </summary>
public interface IWhiteboardService
{
    /// <summary>
    /// Lấy (hoặc tạo mới nếu chưa có) phòng whiteboard của buổi học và sinh room token cho người dùng.
    /// </summary>
    /// <param name="classSessionId">ID buổi học (mỗi buổi 1 phòng).</param>
    /// <param name="isTutor">True = tutor (admin), false = học viên/phụ huynh (writer).</param>
    Task<WhiteboardRoomInfo> GetOrCreateRoomAsync(int classSessionId, bool isTutor, CancellationToken ct = default);
}
