using System.ComponentModel.DataAnnotations;

namespace MV.DomainLayer.DTO.RequestModel
{
    public class NotificationRequest
    {
        [Required(ErrorMessage = "User ID is required.")]
        public string Userid { get; set; } = null!;

        [Required(ErrorMessage = "Title is required.")]
        [StringLength(200, ErrorMessage = "Title cannot exceed 200 characters.")]
        public string Title { get; set; } = null!;

        [Required(ErrorMessage = "Message is required.")]
        [StringLength(1000, ErrorMessage = "Message cannot exceed 1000 characters.")]
        public string Message { get; set; } = null!;

        [StringLength(50, ErrorMessage = "Type cannot exceed 50 characters.")]
        public string? Type { get; set; }

        [RegularExpression(@"^\d+$", ErrorMessage = "Reference ID must be a positive integer.")]
        public string? Referenceid { get; set; }
    }
}
