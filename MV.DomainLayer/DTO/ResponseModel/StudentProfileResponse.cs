namespace MV.DomainLayer.DTO.ResponseModel
{
    public class StudentProfileResponse
    {
        public string StudentId { get; set; }

        public string? ParentId { get; set; }

        public string FullName { get; set; } = null!;

        public DateOnly? BirthDate { get; set; }

        public string? School { get; set; }

        public int? GradeLevelId { get; set; }

        public string? GradeLevel { get; set; }

        public GradeLevelResponse? GradeLevelInfo { get; set; }

        public string? LearningGoals { get; set; }

        public string? AvatarURL { get; set; }

        public string? StudentCode { get; set; }

        public DateTime? StudentCodeExpiresAt { get; set; }

        public DateTime? CreatedAt { get; set; }

        public string? Username { get; set; }

        public int? Age => BirthDate.HasValue && BirthDate.Value.Year > 1
            ? DateTime.Now.Year - BirthDate.Value.Year
            : null;
    }
}
