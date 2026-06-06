using Microsoft.AspNetCore.Http;
using System.ComponentModel.DataAnnotations;

namespace MV.DomainLayer.DTO.RequestModel
{
    public class UpdateTutorAvatarRequest
    {
        /// <summary>
        /// Avatar image file (JPG, PNG). Use [FromForm] on the action to bind.
        /// </summary>
        [Required]
        public IFormFile AvatarFile { get; set; } = default!;
    }
}
