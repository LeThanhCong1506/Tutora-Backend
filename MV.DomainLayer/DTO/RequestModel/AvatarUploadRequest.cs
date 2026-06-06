using Microsoft.AspNetCore.Http;
using MV.DomainLayer.Attributes;
using System.ComponentModel.DataAnnotations;

namespace MV.DomainLayer.DTO.RequestModel
{
    public class AvatarUploadRequest
    {
        [Required(ErrorMessage = "Avatar file is required.")]
        [ImageFile(ErrorMessage = "Avatar must be a valid image (jpg, jpeg, png, webp) under 5MB.")]
        public IFormFile AvatarFile { get; set; } = null!;
    }
}
