using System.ComponentModel.DataAnnotations;

namespace MV.DomainLayer.DTO.RequestModel
{
    public class UpdateStudentRequest
    {
        [Required(ErrorMessage = "Full name must not be empty.")]
        [StringLength(100, MinimumLength = 2, ErrorMessage = "Full name must contain between 2 and 100 characters.")]
        public string Fullname { get; set; } = null!;

        [DataType(DataType.Date)]
        public DateOnly? Birthdate { get; set; }

        [StringLength(255, ErrorMessage = "School name must not exceed 255 characters.")]
        public string? School { get; set; }

        public int? GradeLevelId { get; set; }

        [StringLength(1000, ErrorMessage = "Learning goals must not exceed 1000 characters.")]
        public string? Learninggoals { get; set; }
    }
}
