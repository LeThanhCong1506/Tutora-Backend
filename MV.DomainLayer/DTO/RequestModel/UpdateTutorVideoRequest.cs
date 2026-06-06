using Microsoft.AspNetCore.Http;
using System.ComponentModel.DataAnnotations;

namespace MV.DomainLayer.DTO.RequestModel
{
    public class UpdateTutorVideoRequest
    {
        /// <summary>
        /// File video giới thiệu upload từ local
        /// </summary>
        [Required(ErrorMessage = "Video file is required")]
        public IFormFile VideoFile { get; set; } = null!;
    }
}
