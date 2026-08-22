namespace MV.DomainLayer.DTO.ResponseModel
{
    /// <summary>
    /// 1 bản chỉnh sửa hồ sơ đang chờ Admin duyệt — kèm giá trị hiện tại (đang hiển thị trên
    /// Marketplace) và giá trị đề xuất để FE vẽ diff. Field "Current*" luôn có, field
    /// "Proposed*" chỉ khác null nếu tutor có sửa phần đó ở lần nộp này.
    /// </summary>
    public class PendingProfileUpdateRequestResponse
    {
        public string TutorId { get; set; } = string.Empty;
        public string? TutorFullName { get; set; }
        public string? TutorEmail { get; set; }
        public string? TutorAvatarUrl { get; set; }
        public DateTime SubmittedAt { get; set; }

        public string? CurrentHeadline { get; set; }
        public string? ProposedHeadline { get; set; }

        public string? CurrentTeachingAreaCity { get; set; }
        public string? ProposedTeachingAreaCity { get; set; }

        public string? CurrentTeachingAreaDistrict { get; set; }
        public string? ProposedTeachingAreaDistrict { get; set; }

        public string? CurrentBio { get; set; }
        public string? ProposedBio { get; set; }

        public string? CurrentEducation { get; set; }
        public string? ProposedEducation { get; set; }

        public string? CurrentDegree { get; set; }
        public string? ProposedDegree { get; set; }

        public string? CurrentExperience { get; set; }
        public string? ProposedExperience { get; set; }

        public string? CurrentVideoIntroUrl { get; set; }
        public string? ProposedVideoIntroUrl { get; set; }

        /// <summary>true nếu tutor có đề xuất thay đổi môn học/bảng giá ở lần nộp này.</summary>
        public bool HasProposedSubjectGradePrices { get; set; }

        /// <summary>Bảng giá theo môn/lớp ĐANG SỐNG (hiển thị Marketplace) — luôn có, để FE vẽ diff.</summary>
        public List<TutorSubjectGradePriceResponse> CurrentSubjectGradePrices { get; set; } = new();

        /// <summary>
        /// Bảng giá theo môn/lớp ĐỀ XUẤT ở lần nộp này. Rỗng nếu tutor không đụng tới phần này
        /// (khác <see cref="HasProposedSubjectGradePrices"/> = false).
        /// </summary>
        public List<TutorSubjectGradePriceResponse> ProposedSubjectGradePrices { get; set; } = new();
    }
}
