using Microsoft.AspNetCore.Http;
using MV.ApplicationLayer.ServiceInterfaces;
using System;
using System.Threading.Tasks;

namespace MV.InfrastructureLayer.Services
{
    public class DummyFileStorageService : IFileStorageService
    {
        public Task EnsureBucketExistsAsync(string bucketName)
        {
            return Task.CompletedTask;
        }

        public Task<string> UploadFileAsync(string bucketName, string userId, IFormFile file)
        {
            // Return a dummy URL
            return Task.FromResult($"https://dummy.url/{bucketName}/{userId}/{file.FileName}");
        }

        public Task<bool> DeleteFileAsync(string bucketName, string userId, string filePathOrUrl)
        {
            return Task.FromResult(true);
        }
    }
}
