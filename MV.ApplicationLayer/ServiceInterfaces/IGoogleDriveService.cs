using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace MV.ApplicationLayer.ServiceInterfaces;

/// <summary>
/// Upload + đọc file trên Google Drive qua Drive API (OAuth refresh token).
/// File KHÔNG được cấp quyền public — mọi lượt xem đi qua endpoint proxy có xác thực của app.
/// </summary>
public interface IGoogleDriveService
{
    /// <summary>True nếu tính năng Drive đã bật (GoogleDrive:Enabled).</summary>
    bool Enabled { get; }

    /// <summary>
    /// Upload nội dung (stream) lên Google Drive. File giữ nguyên private (chỉ tài khoản
    /// Drive kết nối mới mở được) — không cấp quyền "anyone with link".
    /// <paramref name="folderId"/> = null → dùng thư mục gốc cấu hình (GoogleDrive:FolderId).
    /// Trả về fileId của file trên Drive.
    /// </summary>
    Task<string> UploadAsync(Stream content, string fileName, string mimeType, string? folderId = null, CancellationToken ct = default);

    /// <summary>
    /// Tìm file đã tồn tại trên Drive theo đúng tên (chưa bị xóa) — dùng để chống upload trùng.
    /// Không giới hạn theo thư mục cha: scope OAuth "drive.file" đã tự đảm bảo chỉ thấy file
    /// do app này tạo, dù file đang nằm trong thư mục con Tutor/Student nào.
    /// Trả về fileId nếu có, null nếu chưa từng upload.
    /// </summary>
    Task<string?> FindFileIdByNameAsync(string fileName, CancellationToken ct = default);

    /// <summary>
    /// Lấy (tạo mới nếu chưa có) id thư mục "{tutorFolderName}/{studentFolderName}" bên trong
    /// thư mục gốc cấu hình — tổ chức recordings theo Tutor &gt; Student thay vì để phẳng.
    /// Tạo lười (lazy): chỉ gọi khi thực sự có buổi ghi hình cần relay lên Drive.
    /// </summary>
    Task<string> GetRecordingFolderAsync(string tutorFolderName, string studentFolderName, CancellationToken ct = default);

    /// <summary>
    /// Đọc nội dung file, chuyển tiếp header Range (nếu có) để hỗ trợ tua video — dùng để proxy
    /// phát lại qua app. KHÔNG cấp quyền public trên Drive; caller tự chịu trách nhiệm xác thực
    /// người xem trước khi gọi hàm này.
    /// </summary>
    Task<DriveMediaResult> GetMediaAsync(string fileId, string? rangeHeader, CancellationToken ct = default);
}

/// <summary>
/// Kết quả tải nội dung file từ Drive — đủ thông tin để proxy nguyên trạng (kể cả 206 Partial
/// Content khi có Range) sang response cho client mà không cần diễn giải thêm.
/// </summary>
public sealed class DriveMediaResult : IDisposable
{
    private readonly IDisposable _underlyingResponse;

    public DriveMediaResult(
        IDisposable underlyingResponse,
        Stream content,
        int statusCode,
        string? contentType,
        long? contentLength,
        string? contentRange,
        bool acceptRanges)
    {
        _underlyingResponse = underlyingResponse;
        Content = content;
        StatusCode = statusCode;
        ContentType = contentType;
        ContentLength = contentLength;
        ContentRange = contentRange;
        AcceptRanges = acceptRanges;
    }

    public int StatusCode { get; }
    public Stream Content { get; }
    public string? ContentType { get; }
    public long? ContentLength { get; }
    public string? ContentRange { get; }
    public bool AcceptRanges { get; }

    public void Dispose() => _underlyingResponse.Dispose();
}
