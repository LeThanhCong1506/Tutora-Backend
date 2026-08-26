using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Google.Apis.Upload;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Configuration;
using DriveData = Google.Apis.Drive.v3.Data;

namespace MV.ApplicationLayer.Services;

/// <summary>
/// Upload file lên Google Drive cá nhân bằng OAuth 2.0 + refresh token.
/// Docs: https://developers.google.com/drive/api/guides/manage-uploads
///
/// File KHÔNG được cấp quyền public: chỉ tài khoản Drive kết nối mới mở trực tiếp được.
/// Mọi lượt xem của Tutor/Student/Parent (và Admin xử lý tranh chấp) đi qua endpoint proxy
/// của app (xem GetMediaAsync + ClassSessionController.GetClassSessionRecordingStream) — quyền
/// truy cập do app tự kiểm tra, không giao cho ACL của Drive.
/// </summary>
public class GoogleDriveService : IGoogleDriveService
{
    private const string DriveMediaBaseUrl = "https://www.googleapis.com/drive/v3/files";
    private const string FolderMimeType = "application/vnd.google-apps.folder";

    private readonly GoogleDriveSettings _settings;
    private readonly ILogger<GoogleDriveService> _logger;

    public GoogleDriveService(IOptions<GoogleDriveSettings> settings, ILogger<GoogleDriveService> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    public bool Enabled => _settings.Enabled;

    public async Task<string> UploadAsync(Stream content, string fileName, string mimeType, string? folderId = null, CancellationToken ct = default)
    {
        var drive = CreateService();

        var targetFolderId = folderId ?? _settings.FolderId;
        var meta = new DriveData.File { Name = fileName };
        if (!string.IsNullOrWhiteSpace(targetFolderId))
            meta.Parents = new[] { targetFolderId };

        // Resumable upload — chịu được file lớn (2-4h), stream thẳng từ S3
        var request = drive.Files.Create(meta, content, mimeType);
        request.Fields = "id";

        var progress = await request.UploadAsync(ct);
        if (progress.Status != UploadStatus.Completed)
            throw new InvalidOperationException(
                $"Upload Google Drive thất bại: {progress.Exception?.Message}", progress.Exception);

        var fileId = request.ResponseBody.Id;

        _logger.LogInformation("Uploaded to Google Drive: fileId={FileId} name={Name} folderId={FolderId}", fileId, fileName, targetFolderId);
        return fileId;
    }

    public async Task<string?> FindFileIdByNameAsync(string fileName, CancellationToken ct = default)
    {
        var drive = CreateService();

        // Escape dấu ' để tránh vỡ cú pháp query (tên chuẩn "session-{id}.mp4" vốn không có, nhưng phòng xa).
        // KHÔNG lọc theo thư mục cha: file có thể nằm trong bất kỳ thư mục con Tutor/Student nào;
        // scope OAuth "drive.file" đã tự đảm bảo chỉ thấy file do app này tạo.
        var safeName = fileName.Replace("\\", "\\\\").Replace("'", "\\'");

        var list = drive.Files.List();
        list.Q = $"name = '{safeName}' and trashed = false";
        list.Spaces = "drive";
        list.Fields = "files(id)";
        list.PageSize = 1;

        var result = await list.ExecuteAsync(ct);
        return result.Files != null && result.Files.Count > 0 ? result.Files[0].Id : null;
    }

    public async Task<string> GetRecordingFolderAsync(string tutorFolderName, string studentFolderName, CancellationToken ct = default)
    {
        var drive = CreateService();
        var tutorFolderId = await GetOrCreateFolderAsync(drive, _settings.FolderId, tutorFolderName, ct);
        return await GetOrCreateFolderAsync(drive, tutorFolderId, studentFolderName, ct);
    }

    public async Task<DriveMediaResult> GetMediaAsync(string fileId, string? rangeHeader, CancellationToken ct = default)
    {
        var drive = CreateService();

        var request = new HttpRequestMessage(HttpMethod.Get, $"{DriveMediaBaseUrl}/{Uri.EscapeDataString(fileId)}?alt=media");
        if (!string.IsNullOrWhiteSpace(rangeHeader))
            request.Headers.TryAddWithoutValidation("Range", rangeHeader);

        // ResponseHeadersRead: không buffer toàn bộ video vào RAM trước khi bắt đầu trả về client
        var response = await drive.HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        try
        {
            var stream = await response.Content.ReadAsStreamAsync(ct);

            return new DriveMediaResult(
                underlyingResponse: response,
                content: stream,
                statusCode: (int)response.StatusCode,
                contentType: response.Content.Headers.ContentType?.ToString(),
                contentLength: response.Content.Headers.ContentLength,
                contentRange: response.Content.Headers.ContentRange?.ToString(),
                acceptRanges: response.Headers.AcceptRanges.Contains("bytes"));
        }
        catch
        {
            // ReadAsStreamAsync lỗi giữa chừng (mạng gián đoạn) → DriveMediaResult chưa kịp tạo
            // để tự dispose response qua "using" ở caller — phải tự giải phóng ở đây.
            response.Dispose();
            throw;
        }
    }

    /// <summary>Tìm (hoặc tạo) 1 thư mục con tên <paramref name="name"/> bên trong <paramref name="parentId"/> (null = My Drive gốc).</summary>
    private static async Task<string> GetOrCreateFolderAsync(DriveService drive, string? parentId, string name, CancellationToken ct)
    {
        var safeName = name.Replace("\\", "\\\\").Replace("'", "\\'");
        var q = $"mimeType = '{FolderMimeType}' and name = '{safeName}' and trashed = false";
        if (!string.IsNullOrWhiteSpace(parentId))
            q += $" and '{parentId}' in parents";

        var list = drive.Files.List();
        list.Q = q;
        list.Spaces = "drive";
        list.Fields = "files(id)";
        list.PageSize = 1;

        var existing = await list.ExecuteAsync(ct);
        if (existing.Files is { Count: > 0 })
            return existing.Files[0].Id;

        var meta = new DriveData.File { Name = name, MimeType = FolderMimeType };
        if (!string.IsNullOrWhiteSpace(parentId))
            meta.Parents = new[] { parentId };

        var createRequest = drive.Files.Create(meta);
        createRequest.Fields = "id";
        var created = await createRequest.ExecuteAsync(ct);
        return created.Id;
    }

    private DriveService CreateService()
    {
        if (string.IsNullOrWhiteSpace(_settings.ClientId)
            || string.IsNullOrWhiteSpace(_settings.ClientSecret)
            || string.IsNullOrWhiteSpace(_settings.RefreshToken))
            throw new InvalidOperationException("GoogleDrive chưa cấu hình ClientId/ClientSecret/RefreshToken.");

        var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
        {
            ClientSecrets = new ClientSecrets
            {
                ClientId = _settings.ClientId,
                ClientSecret = _settings.ClientSecret
            },
            Scopes = new[] { DriveService.Scope.DriveFile }
        });

        // UserCredential với refresh token — tự lấy access token mới mỗi lần cần
        var credential = new UserCredential(flow, "user", new TokenResponse { RefreshToken = _settings.RefreshToken });

        return new DriveService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "Tutora"
        });
    }
}
