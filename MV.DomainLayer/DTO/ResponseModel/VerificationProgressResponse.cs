namespace MV.DomainLayer.DTO.ResponseModel
{
    /// <summary>
    /// Status constants for verification sections
    /// </summary>
    public static class SectionStatus
    {
        public const string InProgress = "in_progress";  // Chưa hoàn thành
        public const string Updated = "updated";         // Đã hoàn thành
    }

    /// <summary>
    /// Response model for tutor verification progress
    /// </summary>
    public class VerificationProgressResponse
    {
        public VerificationSections Sections { get; set; } = new();
    }

    public class VerificationSections
    {
        public VideoSection Video { get; set; } = new();
        public BasicInfoSection BasicInfo { get; set; } = new();
        public IntroductionSection Introduction { get; set; } = new();
        public CertificatesSection Certificates { get; set; } = new();
        public IdentityCardSection IdentityCard { get; set; } = new();
        public PricingSection Pricing { get; set; } = new();
    }

    /// <summary>
    /// Base class for all section info
    /// </summary>
    public abstract class BaseSectionInfo
    {
        public string Status { get; set; } = SectionStatus.InProgress;
        public DateTime? UpdatedAt { get; set; }
    }

    public class VideoSection : BaseSectionInfo
    {
        public string? VideoUrl { get; set; }
    }

    public class BasicInfoSection : BaseSectionInfo
    {
        public string? AvatarUrl { get; set; }
        public string? Headline { get; set; }
        public string? TeachingAreaCity { get; set; }
        public string? TeachingAreaDistrict { get; set; }
        public string? TeachingMode { get; set; }
    }

    public class SubjectInfo
    {
        public int SubjectId { get; set; }
        public string? SubjectName { get; set; }
        public string? GradeLevels { get; set; }
        public string? Tags { get; set; }
    }

    public class IntroductionSection : BaseSectionInfo
    {
        public string? Bio { get; set; }
        public string? Education { get; set; }
        public double? Gpa { get; set; }
        public double? GpaScale { get; set; }
        public string? Experience { get; set; }
    }

    public class CertificatesSection : BaseSectionInfo
    {
        public int TotalCount { get; set; }
        public List<CertificateResponse>? Certificates { get; set; }
    }

    public class IdentityCardSection : BaseSectionInfo
    {
        public string? FrontImageUrl { get; set; }
        public string? BackImageUrl { get; set; }
        public bool IsVerified { get; set; }
    }

    public class PricingSection : BaseSectionInfo
    {
    }
}
