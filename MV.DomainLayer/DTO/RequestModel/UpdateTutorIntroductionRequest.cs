using System.ComponentModel.DataAnnotations;

namespace MV.DomainLayer.DTO.RequestModel
{
    public class UpdateTutorIntroductionRequest
    {
        [Required(ErrorMessage = "Bio is required")]
        [StringLength(2000, MinimumLength = 100, ErrorMessage = "Bio must be 100-2000 characters")]
        public string Bio { get; set; } = string.Empty;

        [Required(ErrorMessage = "Education is required")]
        [StringLength(255, MinimumLength = 10, ErrorMessage = "Education must be 10-255 characters")]
        public string Education { get; set; } = string.Empty;

        [Required(ErrorMessage = "GPA Scale is required")]
        [RegularExpression(@"^(4(\.0)?|10(\.0)?)$", ErrorMessage = "GPA Scale must be 4.0 or 10.0")]
        public double GpaScale { get; set; }

        [Required(ErrorMessage = "GPA is required")]
        [Range(0.0, 10.0, ErrorMessage = "GPA must be between 0.0 and 10.0")]
        public double Gpa { get; set; }

        [Required(ErrorMessage = "Experience is required")]
        [StringLength(2000, MinimumLength = 50, ErrorMessage = "Experience must be 50-2000 characters")]
        public string Experience { get; set; } = string.Empty;
    }
}
