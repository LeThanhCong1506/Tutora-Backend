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
        public string? Degree { get; set; }
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
        public string? IdentityNumberMasked { get; set; }  // Số CCCD đã mã hóa, vd: 079****5678
        public string? FullName { get; set; }              // Họ và tên
        public string? DateOfBirth { get; set; }           // Ngày sinh (dd/MM/yyyy)
        public string? Gender { get; set; }                // Giới tính
        public string? Hometown { get; set; }              // Quê quán (chỉ hiển thị, không có cột riêng)
        public string? PermanentAddress { get; set; }      // Địa chỉ thường trú
        public string? PortraitImageUrl { get; set; }      // Ảnh chân dung (avatar gia sư)
        public bool IsVerified { get; set; }

        /// <summary>
        /// Đã quét CCCD nhưng chủ tài khoản CHƯA xác nhận đưa thông tin vào hồ sơ.
        /// FE dùng để nhắc lại lời mời xác nhận sau khi tải lại trang.
        /// </summary>
        public bool RequiresProfileConfirmation { get; set; }

        /// <summary>Các trường hồ sơ sẽ đổi nếu xác nhận (giá trị hiện tại → giá trị trên CCCD).</summary>
        public List<EkycProfileFieldChange> PendingProfileChanges { get; set; } = new();
    }
}
