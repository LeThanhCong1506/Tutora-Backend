using System.ComponentModel.DataAnnotations;

namespace MV.DomainLayer.DTO.RequestModel
{
    public class StudentSelfLinkRequest
    {
        [Required(ErrorMessage = "Parent code is required.")]
        [StringLength(10, MinimumLength = 6, ErrorMessage = "Parent code must be between 6 and 10 characters.")]
        public string ParentCode { get; set; } = null!;
    }
}
