using System.ComponentModel.DataAnnotations;

namespace MV.DomainLayer.DTO.RequestModel
{
    public class CreateUserRequest
    {
        [Required(ErrorMessage = "Username must not be empty.")]
        [StringLength(50, MinimumLength = 3,
            ErrorMessage = "Username must contain between 3 and 50 characters.")]
        public string Username { get; set; } = null!;

        [Required(ErrorMessage = "Password must not be empty.")]
        [RegularExpression(
            @"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[@$!%*?&])[A-Za-z\d@$!%*?&]{8,}$",
            ErrorMessage = "Password must be at least 8 characters and include uppercase, lowercase, a number, and a special character.")]
        public string Password { get; set; } = null!;

        [Required(ErrorMessage = "Email address must not be empty.")]
        [EmailAddress(ErrorMessage = "Email address is not in a valid format.")]
        public string Email { get; set; } = null!;

        [Required(ErrorMessage = "Full name must not be empty.")]
        [StringLength(100, ErrorMessage = "Full name must not exceed 100 characters.")]
        public string Fullname { get; set; } = null!;

        [Required(ErrorMessage = "Identity number must not be empty.")]
        [RegularExpression(
            @"^\d{12}$",
            ErrorMessage = "Identity number must consist of exactly 12 digits.")]
        public string IdentityNumber { get; set; } = null!;

        [Required(ErrorMessage = "Phone must not be empty.")]
        [Phone(ErrorMessage = "Phone number is not in a valid format.")]
        public string? Phone { get; set; }

        [Required(ErrorMessage = "Date of birth must not be empty.")]
        public DateOnly Birthdate { get; set; }

        [Required(ErrorMessage = "Address must not be empty.")]
        [StringLength(255, ErrorMessage = "Address must not exceed 255 characters.")]
        public string? Address { get; set; }

        [Required(ErrorMessage = "Gender must not be empty.")]
        [Range(0, 2, ErrorMessage = "Gender value is invalid.")]
        public string? Gender { get; set; }

        [Required(ErrorMessage = "User role must not be empty.")]
        public string RoleName { get; set; } = null!; // "Admin", "Tutor", "Student", "Parent"

        public string? ParentId { get; set; } // Dùng khi tạo Student
    }
}
