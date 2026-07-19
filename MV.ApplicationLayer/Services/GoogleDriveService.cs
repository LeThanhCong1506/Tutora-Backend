using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
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
/// </summary>
public class GoogleDriveService : IGoogleDriveService
{
    private readonly GoogleDriveSettings _settings;
    private readonly ILogger<GoogleDriveService> _logger;

    public GoogleDriveService(IOptions<GoogleDriveSettings> settings, ILogger<GoogleDriveService> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    public bool Enabled => _settings.Enabled;

    public async Task<string> UploadAsync(Stream content, string fileName, string mimeType, CancellationToken ct = default)
    {
        var drive = CreateService();

        var meta = new DriveData.File { Name = fileName };
        if (!string.IsNullOrWhiteSpace(_settings.FolderId))
            meta.Parents = new[] { _settings.FolderId };

        // Resumable upload — chịu được file lớn (2-4h), stream thẳng từ S3
        var request = drive.Files.Create(meta, content, mimeType);
        request.Fields = "id, webViewLink";

        var progress = await request.UploadAsync(ct);
        if (progress.Status != UploadStatus.Completed)
            throw new InvalidOperationException(
                $"Upload Google Drive thất bại: {progress.Exception?.Message}", progress.Exception);

        var fileId = request.ResponseBody.Id;

        // Cho phép ai có link đều xem được (để phát lại trong app)
        try
        {
            await drive.Permissions.Create(
                new DriveData.Permission { Type = "anyone", Role = "reader" }, fileId).ExecuteAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Không set được quyền public cho file Drive {FileId}", fileId);
        }

        _logger.LogInformation("Uploaded to Google Drive: fileId={FileId} name={Name}", fileId, fileName);
        return fileId;
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
