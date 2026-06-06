using System.ComponentModel.DataAnnotations;

namespace MV.DomainLayer.DTO.RequestModel
{
    public class SelectTutorSubjectRequest
    {
        [Required(ErrorMessage = "Subject selection is required.")]
        public int SubjectId { get; set; }

        [Required(ErrorMessage = "At least one Grade Level must be selected.")]
        [MinLength(1, ErrorMessage = "Please select at least one Grade Level.")]
        public List<string> GradeLevels { get; set; } = new List<string>();

        [StringLength(500, ErrorMessage = "Tags cannot exceed 500 characters.")]
        public string? Tags { get; set; }
    }
}
