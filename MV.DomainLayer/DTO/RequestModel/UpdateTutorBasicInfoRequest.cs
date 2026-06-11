using System.ComponentModel.DataAnnotations;

namespace MV.DomainLayer.DTO.RequestModel
{
    /// <summary>
    /// Request model for updating tutor basic info (use JSON body).
    /// Avatar should be uploaded separately via PUT /tutor-profile/avatar endpoint.
    /// </summary>
    public class UpdateTutorBasicInfoRequest
    {
        [Required(ErrorMessage = "Headline is required")]
        [StringLength(200, MinimumLength = 10, ErrorMessage = "Headline must be 10-200 characters")]
        public string Headline { get; set; } = string.Empty;

        [Required(ErrorMessage = "City is required")]
        public string TeachingAreaCity { get; set; } = string.Empty;

        [Required(ErrorMessage = "District is required")]
        public string TeachingAreaDistrict { get; set; } = string.Empty;

        [Required(ErrorMessage = "Teaching mode is required")]
        public string TeachingMode { get; set; } = string.Empty;

    }
}
