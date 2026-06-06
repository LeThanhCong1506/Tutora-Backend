using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using MV.ApplicationLayer.ServiceInterfaces;
using Supabase;

namespace MV.ApplicationLayer.Services
{
    public class SupabaseStorageService : ISupabaseStorageService
    {
        private readonly Client _supabaseClient;

        public SupabaseStorageService(Client supabaseClient, IConfiguration configuration)
        {
            _supabaseClient = supabaseClient;
        }

        public async Task EnsureBucketExistsAsync(string bucketName)
        {
            try
            {
                var buckets = await _supabaseClient.Storage.ListBuckets();
                var bucketExists = buckets?.Any(b => b.Name == bucketName) ?? false;

                if (!bucketExists)
                {
                    await _supabaseClient.Storage.CreateBucket(bucketName);
                    Console.WriteLine($"✅ Supabase bucket '{bucketName}' đã được tạo!");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Lỗi khi kiểm tra/tạo bucket '{bucketName}': {ex.Message}");
            }
        }

        public async Task<string> UploadFileAsync(string bucketName, string userId, IFormFile file)
        {
            // 1. Tạo tên file unique
            var fileExtension = Path.GetExtension(file.FileName);
            var uniqueFileName = $"{Guid.NewGuid()}{fileExtension}";

            // Đường dẫn file: userId/uniqueFileName
            var filePath = $"{userId}/{uniqueFileName}";

            using var memoryStream = new MemoryStream();
            await file.CopyToAsync(memoryStream);
            var fileBytes = memoryStream.ToArray();

            // 2. Upload lên Supabase Storage
            await _supabaseClient.Storage
                .From(bucketName)
                .Upload(fileBytes, filePath, new Supabase.Storage.FileOptions
                {
                    ContentType = file.ContentType,
                    Upsert = true
                });

            // 3. Tạo Signed URL có thời hạn 1 năm
            // Tính toán số giây trong 1 năm: 365 ngày * 24 giờ * 60 phút * 60 giây
            int oneYearInSeconds = 31536000;

            var signedUrl = await _supabaseClient.Storage
                .From(bucketName)
                .CreateSignedUrl(filePath, oneYearInSeconds);

            return signedUrl;
        }

        public async Task<bool> DeleteFileAsync(string bucketName, string userId, string fileName)
        {
            try
            {
                // Đường dẫn file: userId/fileName (giống như UploadFileAsync)
                var filePath = $"{userId}/{fileName}";

                await _supabaseClient.Storage
                    .From(bucketName)
                    .Remove(new List<string> { filePath });
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}