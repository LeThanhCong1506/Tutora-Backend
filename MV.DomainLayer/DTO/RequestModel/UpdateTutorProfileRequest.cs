using System.ComponentModel.DataAnnotations;

namespace MV.DomainLayer.DTO.RequestModel
{
    public class UpdateTutorProfileRequest
    {
        [StringLength(200, MinimumLength = 10, ErrorMessage = "Headline must be between 10 and 200 characters.")]
        public string? Headline { get; set; }

        [StringLength(5000, MinimumLength = 50, ErrorMessage = "Bio must be at least 50 characters.")]
        public string? Bio { get; set; }

        [Range(1000, 100000000, ErrorMessage = "Hourly Rate must be between 1,000 and 100,000,000 VND.")]
        public decimal? HourlyRate { get; set; }

        [StringLength(255, ErrorMessage = "Education cannot exceed 255 characters.")]
        public string? Education { get; set; }

        [StringLength(2000, ErrorMessage = "Experience cannot exceed 2000 characters.")]
        public string? Experience { get; set; }

        [Range(0.0, 10.0, ErrorMessage = "GPA must be between 0.0 and 10.0")]
        public double? Gpa { get; set; }

        [Range(4.0, 10.0, ErrorMessage = "GPA Scale must be standard (e.g., 4.0 or 10.0)")]
        public double? GpaScale { get; set; }

        // Lưu ý: CertificateUrl có thể là chuỗi JSON hoặc link ảnh, tùy cách bạn lưu. Ở đây để string.
        public string? CertificateUrl { get; set; }

        [Url(ErrorMessage = "Video Intro must be a valid URL.")]
        public string? VideoIntroUrl { get; set; }

        [RegularExpression("^(Online|Offline|Hybrid)$", ErrorMessage = "Teaching mode must be: 'Online', 'Offline', or 'Hybrid'.")]
        public string? TeachingMode { get; set; }

        [StringLength(50)]
        public string? TeachingAreaCity { get; set; }

        [StringLength(50)]
        public string? TeachingAreaDistrict { get; set; }
    }
}
