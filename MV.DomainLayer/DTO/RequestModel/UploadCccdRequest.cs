using Microsoft.AspNetCore.Http;
using System.ComponentModel.DataAnnotations;

namespace MV.DomainLayer.DTO.RequestModel;

public class UploadCccdRequest
{
    [Required(ErrorMessage = "Ảnh mặt trước CCCD là bắt buộc.")]
    public IFormFile FrontImage { get; set; } = null!;

    [Required(ErrorMessage = "Ảnh mặt sau CCCD là bắt buộc.")]
    public IFormFile BackImage { get; set; } = null!;
}
