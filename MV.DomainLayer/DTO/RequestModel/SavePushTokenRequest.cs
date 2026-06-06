using System.ComponentModel.DataAnnotations;

namespace MV.DomainLayer.DTO.RequestModel
{
    public class SavePushTokenRequest
    {
        [Required(ErrorMessage = "FcmToken is required.")]
        [StringLength(500, ErrorMessage = "FcmToken must not exceed 500 characters.")]
        public string? FcmToken { get; set; }
    }
}
