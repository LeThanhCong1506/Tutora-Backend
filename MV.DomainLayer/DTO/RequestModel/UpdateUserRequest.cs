using System.ComponentModel.DataAnnotations;
using MV.DomainLayer.Enums;

namespace MV.DomainLayer.DTO.RequestModel
{
    public class UpdateUserRequest
    {
        [Required(ErrorMessage = "Full name must not be empty.")]
        [StringLength(100, ErrorMessage = "Full name must not exceed 100 characters.")]
        public string Fullname { get; set; } = null!;

        [Required(ErrorMessage = "Date of birth must not be empty.")]
        public DateOnly Birthdate { get; set; }

        [Required(ErrorMessage = "Address must not be empty.")]
        [StringLength(255, ErrorMessage = "Address must not exceed 255 characters.")]
        public string? Address { get; set; }

        [Required(ErrorMessage = "Gender must not be empty.")]
        [EnumDataType(typeof(Gender), ErrorMessage = "Gender value is invalid.")]
        public Gender? Gender { get; set; }

        public string? Avatarurl { get; set; }
    }
}
