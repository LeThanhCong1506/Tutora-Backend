namespace MV.ApplicationLayer.Helpers;

/// <summary>
/// Suy ra trạng thái bản ghi video từ các cột của ClassSession (không có cột status riêng):
/// có url → available; còn s3key → processing (đang relay lên Drive); còn sid → recording (đang ghi);
/// còn lại → none. Dùng chung cho mọi nơi hiển thị trạng thái recording (dispute, xem lại buổi học)
/// để ngữ nghĩa nhất quán giữa các luồng.
/// </summary>
public static class RecordingStatusResolver
{
    /// <param name="roomClosed">
    /// Phòng học đã đóng (Checkouttime đã có) chưa. Recordingurl/Recordings3key CHỈ được
    /// TryStopRecordingAsync gán lúc checkout — nếu phòng chưa đóng mà 2 cột này vẫn có giá trị
    /// thì chắc chắn là dữ liệu cũ còn sót lại từ một lượt ghi hình trước đó của CÙNG classSessionId
    /// (vd bị reset trạng thái để test lại) → không được tin, kẻo hiện nhầm link/video cũ cho
    /// buổi đang diễn ra hiện tại.
    /// </param>
    public static (string Status, string? Url) Resolve(string? url, string? s3key, string? sid, bool roomClosed)
    {
        if (!roomClosed)
            return !string.IsNullOrEmpty(sid) ? ("recording", null) : ("none", null);

        if (!string.IsNullOrEmpty(url)) return ("available", url);
        if (!string.IsNullOrEmpty(s3key)) return ("processing", null);
        if (!string.IsNullOrEmpty(sid)) return ("recording", null);
        return ("none", null);
    }
}
