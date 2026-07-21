using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace MV.ApplicationLayer.ServiceInterfaces;

/// <summary>Upload file lên Google Drive qua Drive API (OAuth refresh token).</summary>
public interface IGoogleDriveService
{
    /// <summary>True nếu tính năng Drive đã bật (GoogleDrive:Enabled).</summary>
    bool Enabled { get; }

    /// <summary>
    /// Upload nội dung (stream) lên Google Drive và mở quyền xem theo link.
    /// Trả về fileId của file trên Drive.
    /// </summary>
    Task<string> UploadAsync(Stream content, string fileName, string mimeType, CancellationToken ct = default);

    /// <summary>
    /// Tìm file đã tồn tại trên Drive theo đúng tên (trong folder cấu hình, chưa bị xóa).
    /// Trả về fileId nếu có, null nếu chưa từng upload — dùng để chống upload trùng.
    /// </summary>
    Task<string?> FindFileIdByNameAsync(string fileName, CancellationToken ct = default);
}
