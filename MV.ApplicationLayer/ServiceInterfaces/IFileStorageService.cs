using Microsoft.AspNetCore.Http;
using System.Threading.Tasks;

namespace MV.ApplicationLayer.ServiceInterfaces
{
    public interface IFileStorageService
    {
        /// <summary>
        /// Ensure the bucket or root folder exists (if applicable to the storage provider).
        /// </summary>
        Task EnsureBucketExistsAsync(string bucketName);

        /// <summary>
        /// Upload a file to the storage provider.
        /// </summary>
        /// <param name="bucketName">The root folder or bucket name.</param>
        /// <param name="userId">The sub-folder name (e.g., user ID).</param>
        /// <param name="file">The file to upload.</param>
        /// <returns>The public URL of the uploaded file.</returns>
        Task<string> UploadFileAsync(string bucketName, string userId, IFormFile file);

        /// <summary>
        /// Upload ảnh từ bytes (vd ảnh crop từ PDF). Trả về public URL.
        /// </summary>
        Task<string> UploadImageBytesAsync(string bucketName, string userId, byte[] bytes, string fileName);

        /// <summary>
        /// Upload a file dưới dạng private (authenticated). Dùng cho CCCD và tài liệu nhạy cảm.
        /// URL trả về chỉ là public ID, cần gọi GenerateSignedUrl để lấy link xem tạm thời.
        /// </summary>
        Task<string> UploadPrivateFileAsync(string bucketName, string userId, IFormFile file);

        /// <summary>
        /// Tạo signed URL có thời hạn cho file private. Dùng khi admin/owner cần xem CCCD.
        /// </summary>
        /// <param name="publicIdOrUrl">Public ID hoặc URL Cloudinary của file.</param>
        /// <param name="expiresInMinutes">Thời gian hiệu lực (mặc định 15 phút).</param>
        string GenerateSignedUrl(string publicIdOrUrl, int expiresInMinutes = 15);

        /// <summary>
        /// Delete a file from the storage provider.
        /// </summary>
        Task<bool> DeleteFileAsync(string bucketName, string userId, string filePathOrUrl);
    }
}
