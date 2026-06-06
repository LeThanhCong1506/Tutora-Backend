using System.ComponentModel.DataAnnotations;

namespace MV.DomainLayer.DTO.RequestModel
{
    public class AdminUpdateUserRequest
    {
        [StringLength(100, ErrorMessage = "Full name must not exceed 100 characters.")]
        public string? Fullname { get; set; }

        [EmailAddress(ErrorMessage = "Invalid email format.")]
        public string? Email { get; set; }

        [Phone(ErrorMessage = "Invalid phone format.")]
        public string? Phone { get; set; }

        public string? Primaryrole { get; set; }

        public int? Status { get; set; }

        [StringLength(255, ErrorMessage = "Address must not exceed 255 characters.")]
        public string? Address { get; set; }

        public string? Gender { get; set; }

        public string? Avatarurl { get; set; }
    }
}
