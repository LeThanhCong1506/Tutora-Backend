using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Configuration;
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace MV.InfrastructureLayer.Services
{
    public class CloudinaryStorageService : IFileStorageService
    {
        private readonly Cloudinary _cloudinary;
        private readonly ILogger<CloudinaryStorageService> _logger;

        public CloudinaryStorageService(IOptions<CloudinarySettings> cloudinaryConfig, ILogger<CloudinaryStorageService> logger)
        {
            var acc = new Account(
                cloudinaryConfig.Value.CloudName,
                cloudinaryConfig.Value.ApiKey,
                cloudinaryConfig.Value.ApiSecret
            );
            _cloudinary = new Cloudinary(acc);
            _logger = logger;
        }

        public Task EnsureBucketExistsAsync(string bucketName)
        {
            // Cloudinary creates folders dynamically upon upload, no need to explicitly create buckets
            return Task.CompletedTask;
        }

        public async Task<string> UploadFileAsync(string bucketName, string userId, IFormFile file)
        {
            if (file == null || file.Length == 0)
            {
                throw new ArgumentException("File is empty or null.");
            }

            var folderPath = string.IsNullOrWhiteSpace(userId) ? bucketName : $"{bucketName}/{userId}";

            using var stream = file.OpenReadStream();
            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            // PDF upload dưới dạng image để tránh Cloudinary chặn raw delivery
            var isImage = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp", ".tiff", ".pdf" }.Contains(extension);
            var isVideo = new[] { ".mp4", ".mov", ".avi", ".wmv", ".flv", ".webm" }.Contains(extension);

            UploadResult uploadResult;

            var publicId = $"{Guid.NewGuid()}_{Path.GetFileNameWithoutExtension(file.FileName)}";

            if (isImage)
            {
                var uploadParams = new ImageUploadParams
                {
                    File = new FileDescription(file.FileName, stream),
                    Folder = folderPath,
                    PublicId = publicId,
                    AccessMode = "public"
                };
                uploadResult = await _cloudinary.UploadAsync(uploadParams);
            }
            else if (isVideo)
            {
                var uploadParams = new VideoUploadParams
                {
                    File = new FileDescription(file.FileName, stream),
                    Folder = folderPath,
                    PublicId = publicId,
                    AccessMode = "public"
                };
                uploadResult = await _cloudinary.UploadAsync(uploadParams);
            }
            else
            {
                var uploadParams = new RawUploadParams
                {
                    File = new FileDescription(file.FileName, stream),
                    Folder = folderPath,
                    PublicId = publicId,
                    AccessMode = "public"
                };
                uploadResult = await _cloudinary.UploadAsync(uploadParams);
            }

            if (uploadResult.Error != null)
            {
                _logger.LogError("Cloudinary upload failed: {Error}", uploadResult.Error.Message);
                throw new Exception($"Cloudinary upload failed: {uploadResult.Error.Message}");
            }

            return uploadResult.SecureUrl.ToString();
        }

        public async Task<string> UploadPrivateFileAsync(string bucketName, string userId, IFormFile file)
        {
            if (file == null || file.Length == 0)
                throw new ArgumentException("File is empty or null.");

            var folderPath = string.IsNullOrWhiteSpace(userId) ? bucketName : $"{bucketName}/{userId}";

            using var stream = file.OpenReadStream();
            var publicId = $"{Guid.NewGuid()}_{Path.GetFileNameWithoutExtension(file.FileName)}";

            var uploadParams = new ImageUploadParams
            {
                File       = new FileDescription(file.FileName, stream),
                Folder     = folderPath,
                PublicId   = publicId,
                AccessMode = "authenticated"   // private — không ai truy cập trực tiếp được
            };

            var uploadResult = await _cloudinary.UploadAsync(uploadParams);

            if (uploadResult.Error != null)
            {
                _logger.LogError("Cloudinary private upload failed: {Error}", uploadResult.Error.Message);
                throw new Exception($"Cloudinary upload failed: {uploadResult.Error.Message}");
            }

            // Trả về SecureUrl (authenticated — chỉ dùng để lưu DB, không share trực tiếp)
            return uploadResult.SecureUrl.ToString();
        }

        public string GenerateSignedUrl(string publicIdOrUrl, int expiresInMinutes = 15)
        {
            // Trích public ID từ URL nếu cần
            var publicId = publicIdOrUrl.Contains("/upload/")
                ? ExtractPublicIdFromUrl(publicIdOrUrl)
                : publicIdOrUrl;

            var expireAt = DateTimeOffset.UtcNow.AddMinutes(expiresInMinutes).ToUnixTimeSeconds();

            var transformation = new Transformation();
            var signedUrl = _cloudinary.Api.UrlImgUp
                .Signed(true)
                .Transform(transformation)
                .Secure(true)
                .BuildUrl($"{publicId}?_expires={expireAt}");

            return signedUrl;
        }

        public async Task<bool> DeleteFileAsync(string bucketName, string userId, string filePathOrUrl)
        {
            try
            {
                var publicId = ExtractPublicIdFromUrl(filePathOrUrl);
                if (string.IsNullOrEmpty(publicId))
                {
                    _logger.LogWarning("Could not extract public ID from URL: {Url}", filePathOrUrl);
                    return false;
                }

                var resourceType = ResourceType.Image;
                if (filePathOrUrl.Contains("/video/upload/")) resourceType = ResourceType.Video;
                else if (filePathOrUrl.Contains("/raw/upload/")) resourceType = ResourceType.Raw;

                var deletionParams = new DeletionParams(publicId) { ResourceType = resourceType };
                var result = await _cloudinary.DestroyAsync(deletionParams);

                return result.Result == "ok";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete file from Cloudinary: {Url}", filePathOrUrl);
                return false;
            }
        }

        private string ExtractPublicIdFromUrl(string url)
        {
            // Regex to extract public ID from Cloudinary URL
            var match = Regex.Match(url, @"/upload/(?:v\d+/)?(.+?)(?:\.[^.]+)?$");
            if (match.Success)
            {
                return match.Groups[1].Value;
            }

            return url;
        }
    }
}
