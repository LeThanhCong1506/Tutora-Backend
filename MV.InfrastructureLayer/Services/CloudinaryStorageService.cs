using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Configuration;
using System.Text.RegularExpressions;

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
            var isImage = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp", ".tiff" }.Contains(extension);
            var isVideo = new[] { ".mp4", ".mov", ".avi", ".wmv", ".flv", ".webm" }.Contains(extension);

            UploadResult uploadResult;

            if (isImage)
            {
                var uploadParams = new ImageUploadParams
                {
                    File = new FileDescription(file.FileName, stream),
                    Folder = folderPath,
                    PublicId = $"{Guid.NewGuid()}_{Path.GetFileNameWithoutExtension(file.FileName)}"
                };
                uploadResult = await _cloudinary.UploadAsync(uploadParams);
            }
            else if (isVideo)
            {
                var uploadParams = new VideoUploadParams
                {
                    File = new FileDescription(file.FileName, stream),
                    Folder = folderPath,
                    PublicId = $"{Guid.NewGuid()}_{Path.GetFileNameWithoutExtension(file.FileName)}"
                };
                uploadResult = await _cloudinary.UploadAsync(uploadParams);
            }
            else
            {
                var uploadParams = new RawUploadParams
                {
                    File = new FileDescription(file.FileName, stream),
                    Folder = folderPath,
                    PublicId = $"{Guid.NewGuid()}_{Path.GetFileNameWithoutExtension(file.FileName)}"
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
            // Format usually: https://res.cloudinary.com/<cloud_name>/<resource_type>/<type>/<version>/<public_id>.<ext>
            // We want everything after upload/v.../ or just after upload/ without version
            
            var match = Regex.Match(url, @"/upload/(?:v\d+/)?(.+?)(?:\.[^.]+)?$");
            if (match.Success)
            {
                return match.Groups[1].Value;
            }

            // Fallback if the string provided is already just a public ID or path
            return url;
        }
    }
}
