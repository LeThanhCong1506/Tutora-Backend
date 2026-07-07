using MV.DomainLayer.DTO;

namespace MV.ApplicationLayer.ServiceInterfaces
{
    /// <summary>
    /// Lưu/đọc bản đề xuất chỉnh sửa hồ sơ (Tutorprofile) đang chờ Admin duyệt, dùng Redis
    /// làm nơi lưu trữ duy nhất — không đụng tới bảng tutor_profiles cho đến khi được duyệt.
    /// </summary>
    public interface ITutorProfileUpdateStagingService
    {
        Task<PendingTutorProfileUpdate?> GetPendingUpdateAsync(string tutorId);

        /// <summary>
        /// Giống GetPendingUpdateAsync nhưng trả kèm chuỗi JSON thô đã đọc — dùng làm "phiên bản
        /// đã thấy" để ClearPendingUpdateIfUnchangedAsync so sánh trước khi xoá, tránh xoá nhầm
        /// một bản mới hơn mà Tutor vừa nộp trong lúc Admin đang xử lý (race condition).
        /// </summary>
        Task<(PendingTutorProfileUpdate? Data, string? RawJson)> GetPendingUpdateWithRawAsync(string tutorId);

        /// <summary>
        /// Tạo mới hoặc ghi đè lên bản đang chờ duyệt hiện có của tutor (nếu có) — đúng 1 bản
        /// pending cho mỗi tutor tại 1 thời điểm. applyChanges chỉ nên set các field vừa được
        /// tutor chỉnh sửa trong lần gọi này; các field khác giữ nguyên giá trị null (= "chưa
        /// đụng tới") để không ghi đè nhầm lên phần hồ sơ không liên quan khi Admin approve.
        /// </summary>
        Task UpsertPendingUpdateAsync(string tutorId, Action<PendingTutorProfileUpdate> applyChanges);

        /// <summary> Xoá bản đang chờ duyệt sau khi Admin approve/reject. An toàn khi gọi nhiều lần.</summary>
        Task ClearPendingUpdateAsync(string tutorId);

        /// <summary>
        /// Chỉ xoá bản đang chờ duyệt NẾU nội dung vẫn giống hệt <paramref name="expectedRawJson"/>
        /// (giá trị lấy từ GetPendingUpdateWithRawAsync tại thời điểm Admin đọc để xử lý). Dùng
        /// Redis transaction có điều kiện (compare-and-delete) — nguyên tử, không cần lock ngoài.
        /// Trả false nếu Tutor đã ghi thêm thay đổi mới trong lúc Admin đang xử lý: bản mới đó
        /// vẫn được GIỮ LẠI trong Redis (không bị xoá theo), để lần duyệt sau tự thấy.
        /// </summary>
        Task<bool> ClearPendingUpdateIfUnchangedAsync(string tutorId, string expectedRawJson);

        Task<List<string>> GetAllPendingTutorIdsAsync();
    }
}
