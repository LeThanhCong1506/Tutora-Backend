using Microsoft.AspNetCore.Http;

namespace MV.ApplicationLayer.ServiceInterfaces
{
    public interface ISupabaseStorageService
    {
        /// <summary>
        /// Create the Supabase Storage bucket if it does not already exist.
        /// Called once at startup or on first upload for a given bucket.
        /// </summary>
        Task EnsureBucketExistsAsync(string bucketName);
        /// <summary>
        /// Upload file lên Supabase Storage
        /// </summary>
        /// <param name="bucketName">Tên bucket</param>
        /// <param name="userId">ID người dùng (dùng làm folder)</param>
        /// <param name="file">File cần upload</param>
        /// <returns>Public URL của file đã upload</returns>
        Task<string> UploadFileAsync(string bucketName, string userId, IFormFile file);

        /// <summary>
        /// Xóa file trên Supabase Storage
        /// </summary>
        Task<bool> DeleteFileAsync(string bucketName, string userId, string filePath);
    }
}
