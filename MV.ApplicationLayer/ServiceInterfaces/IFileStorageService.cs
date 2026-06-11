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
        /// Delete a file from the storage provider.
        /// </summary>
        /// <param name="bucketName">The root folder or bucket name.</param>
        /// <param name="userId">The sub-folder name (e.g., user ID).</param>
        /// <param name="filePathOrUrl">The file path, public ID, or URL to delete.</param>
        /// <returns>True if deleted successfully, otherwise false.</returns>
        Task<bool> DeleteFileAsync(string bucketName, string userId, string filePathOrUrl);
    }
}
