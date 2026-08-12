using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Configuration;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace MV.InfrastructureLayer.Services
{
    /// <summary>
    /// Lưu file trực tiếp trên đĩa VPS thay vì Cloudinary. File public serve qua static files middleware
    /// (xem Program.cs, request path = LocalStorageSettings.PublicRequestPath). File private KHÔNG serve
    /// tĩnh — chỉ đọc được qua PrivateFileController sau khi xác thực chữ ký HMAC + hạn dùng trong URL,
    /// tương đương cơ chế signed URL của Cloudinary.
    /// </summary>
    public class LocalFileStorageService : IFileStorageService
    {
        private readonly LocalStorageSettings _settings;
        private readonly ILogger<LocalFileStorageService> _logger;

        public LocalFileStorageService(IOptions<LocalStorageSettings> settings, ILogger<LocalFileStorageService> logger)
        {
            _settings = settings.Value;
            _logger = logger;
        }

        public Task EnsureBucketExistsAsync(string bucketName)
        {
            Directory.CreateDirectory(Path.Combine(_settings.PublicRoot, bucketName));
            return Task.CompletedTask;
        }

        public async Task<string> UploadFileAsync(string bucketName, string userId, IFormFile file)
        {
            if (file == null || file.Length == 0)
                throw new ArgumentException("File is empty or null.");

            using var stream = file.OpenReadStream();
            return await SavePublicAsync(bucketName, userId, stream, file.FileName);
        }

        public async Task<string> UploadImageBytesAsync(string bucketName, string userId, byte[] bytes, string fileName)
        {
            if (bytes == null || bytes.Length == 0)
                throw new ArgumentException("Image bytes empty.");

            using var stream = new MemoryStream(bytes);
            return await SavePublicAsync(bucketName, userId, stream, fileName);
        }

        public async Task<string> UploadPrivateFileAsync(string bucketName, string userId, IFormFile file)
        {
            if (file == null || file.Length == 0)
                throw new ArgumentException("File is empty or null.");

            var relativePath = BuildRelativePath(bucketName, userId, file.FileName);
            var fullPath = Path.Combine(_settings.PrivateRoot, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

            using (var source = file.OpenReadStream())
            using (var dest = new FileStream(fullPath, FileMode.Create, FileAccess.Write))
            {
                await source.CopyToAsync(dest);
            }

            _logger.LogInformation("Đã lưu file private: {RelativePath}", relativePath);
            // Trả về đường dẫn tương đối (không phải path đầy đủ trên đĩa) — dùng làm input cho
            // GenerateSignedUrl sau này, giống PublicId của Cloudinary.
            return relativePath.Replace('\\', '/');
        }

        public string GenerateSignedUrl(string publicIdOrUrl, int expiresInMinutes = 15)
        {
            if (string.IsNullOrWhiteSpace(publicIdOrUrl))
                return string.Empty;

            var relativePath = publicIdOrUrl.Replace('\\', '/');
            var expiresAt = DateTimeOffset.UtcNow.AddMinutes(expiresInMinutes).ToUnixTimeSeconds();
            var signature = Convert.ToHexString(ComputeSignature(relativePath, expiresAt, _settings.SigningKey)).ToLowerInvariant();

            var query = $"path={Uri.EscapeDataString(relativePath)}&expires={expiresAt}&sig={signature}";
            return $"{_settings.PublicBaseUrl}/api/files/private?{query}";
        }

        public Task<bool> DeleteFileAsync(string bucketName, string userId, string filePathOrUrl)
        {
            try
            {
                var fullPath = ResolvePhysicalPath(filePathOrUrl);
                if (fullPath == null || !File.Exists(fullPath))
                    return Task.FromResult(false);

                File.Delete(fullPath);
                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete local file: {Path}", filePathOrUrl);
                return Task.FromResult(false);
            }
        }

        /// <summary>Tính chữ ký HMAC-SHA256 cho (path, thời điểm hết hạn) — dùng chung giữa lúc tạo signed
        /// URL (ở đây) và lúc xác thực (PrivateFileController), nên phải giữ đúng định dạng chuỗi ký.</summary>
        public static byte[] ComputeSignature(string relativePath, long expiresAtUnixSeconds, string signingKey)
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(signingKey));
            return hmac.ComputeHash(Encoding.UTF8.GetBytes($"{relativePath}|{expiresAtUnixSeconds}"));
        }

        private async Task<string> SavePublicAsync(string bucketName, string userId, Stream content, string originalFileName)
        {
            var relativePath = BuildRelativePath(bucketName, userId, originalFileName);
            var fullPath = Path.Combine(_settings.PublicRoot, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

            using (var dest = new FileStream(fullPath, FileMode.Create, FileAccess.Write))
            {
                await content.CopyToAsync(dest);
            }

            var urlPath = string.Join('/', Array.ConvertAll(relativePath.Split(Path.DirectorySeparatorChar), Uri.EscapeDataString));
            return $"{_settings.PublicBaseUrl}{_settings.PublicRequestPath}/{urlPath}";
        }

        /// <summary>bucket/userId/{guid}_{tên gốc đã làm sạch} — Path.GetFileName loại bỏ mọi thành phần
        /// thư mục trong tên gốc để chặn path traversal qua tên file người dùng upload lên.</summary>
        private static string BuildRelativePath(string bucketName, string userId, string originalFileName)
        {
            var safeFileName = SanitizeFileNamePart(Path.GetFileName(originalFileName).Trim());
            var safeBucket = SanitizeFileNamePart(bucketName);
            var safeUserId = SanitizeFileNamePart(userId);
            var fileName = $"{Guid.NewGuid():N}_{safeFileName}";
            return string.IsNullOrWhiteSpace(safeUserId)
                ? Path.Combine(safeBucket, fileName)
                : Path.Combine(safeBucket, safeUserId, fileName);
        }

        private static string SanitizeFileNamePart(string value)
        {
            var result = value;
            foreach (var c in Path.GetInvalidFileNameChars())
                result = result.Replace(c, '_');
            return result;
        }

        /// <summary>Thử resolve theo cả public lẫn private root, chặn path traversal bằng cách bắt buộc
        /// kết quả phải nằm bên trong đúng root tương ứng.</summary>
        private string? ResolvePhysicalPath(string filePathOrUrl)
        {
            string relativePath;
            string root;

            var publicPrefix = $"{_settings.PublicBaseUrl}{_settings.PublicRequestPath}/";
            if (filePathOrUrl.StartsWith(publicPrefix, StringComparison.Ordinal))
            {
                relativePath = Uri.UnescapeDataString(filePathOrUrl[publicPrefix.Length..]);
                root = _settings.PublicRoot;
            }
            else
            {
                relativePath = filePathOrUrl;
                root = _settings.PrivateRoot;
            }

            var rootFullPath = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
            var candidate = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
            return candidate.StartsWith(rootFullPath, StringComparison.Ordinal) ? candidate : null;
        }
    }
}
