using MV.DomainLayer.Enums;

namespace MV.DomainLayer.DTO.ResponseModel
{
    /// <summary>
    /// Response model dành riêng cho danh sách Tutor chờ duyệt.
    /// Bao gồm cả thông tin User và Verification Sections để Admin có thể review.
    /// </summary>
    public class PendingTutorResponse
    {
        // --- User Info ---
        public string Userid { get; set; } = null!;
        public string? Username { get; set; }
        public string? Fullname { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? Avatarurl { get; set; }
        public DateOnly? Birthdate { get; set; }
        public Gender? Gender { get; set; }
        public string? Address { get; set; }
        public int? Status { get; set; }
        public DateTime? Createdat { get; set; }

        // --- Profile Status ---
        public string? ProfileStatus { get; set; }
        public string? RejectionNote { get; set; }
        public DateTime? ProfileCreatedAt { get; set; }
        public DateTime? ProfileUpdatedAt { get; set; }

        // --- Verification Sections (giống GetVerificationProgress) ---
        public VerificationSections Sections { get; set; } = new();
    }
}
