namespace MV.ApplicationLayer.Helpers;

/// <summary>
/// Suy ra trạng thái bản ghi video từ các cột của ClassSession (không có cột status riêng):
/// có url → available; còn s3key → processing (đang relay lên Drive); còn sid mà phòng chưa đóng
/// → recording (đang ghi); còn sid khi phòng đã đóng → failed (ghi hình hỏng); còn lại → none.
/// Dùng chung cho mọi nơi hiển thị trạng thái recording (dispute, xem lại buổi học) để ngữ nghĩa
/// nhất quán giữa các luồng.
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

        // Phòng đã đóng mà vẫn còn sid, không có file nào: TryStopRecordingAsync đã chạy nhưng
        // Agora không trả về file (stop lỗi, hoặc fileList rỗng vì recorder đã tự thoát theo
        // maxIdleTime). KHÔNG được trả "recording" — phòng đóng rồi thì không thể còn đang ghi,
        // để vậy thì UI treo mãi ở "Đang ghi hình" (xem buổi 515/516 trên dev).
        if (!string.IsNullOrEmpty(sid)) return ("failed", null);
        return ("none", null);
    }
}
