using System.ComponentModel.DataAnnotations;

namespace MV.DomainLayer.DTO.RequestModel
{
    public class UpdateTutorVideoRequest
    {
        [Required(ErrorMessage = "Link video là bắt buộc")]
        [Url(ErrorMessage = "Link video không hợp lệ")]
        [StringLength(500, ErrorMessage = "Link video quá dài")]
        public string VideoUrl { get; set; } = string.Empty;
    }
}
