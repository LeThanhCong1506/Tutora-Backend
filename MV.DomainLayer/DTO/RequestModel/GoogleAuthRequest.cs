using System.ComponentModel.DataAnnotations;

namespace MV.DomainLayer.DTO.RequestModel
{
    public class GoogleAuthRequest
    {
        [Required(ErrorMessage = "Google ID Token is required.")]
        public string IdToken { get; set; } = string.Empty;
    }
}
