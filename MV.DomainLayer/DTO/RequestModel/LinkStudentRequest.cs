using System.ComponentModel.DataAnnotations;

namespace MV.DomainLayer.DTO.RequestModel
{
    public class LinkStudentRequest
    {
        [Required(ErrorMessage = "Link code is required.")]
        [StringLength(10, MinimumLength = 6, ErrorMessage = "Link code must be between 6 and 10 characters.")]
        public string Code { get; set; } = null!;
    }
}
