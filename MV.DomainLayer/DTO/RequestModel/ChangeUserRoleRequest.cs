using System.ComponentModel.DataAnnotations;
using MV.DomainLayer.Constants;

namespace MV.DomainLayer.DTO.RequestModel
{
    /// <summary>
    /// Request để Admin thay đổi role của một user.
    /// Chỉ cho phép set các role: Parent, Student, Tutor, Staff.
    /// Role Admin không thể gán qua API.
    /// </summary>
    public class ChangeUserRoleRequest
    {
        /// <summary>
        /// Role mới cần gán cho user.
        /// Các giá trị hợp lệ: "Parent", "Student", "Tutor", "Staff".
        /// </summary>
        [Required(ErrorMessage = "Role không được để trống.")]
        public string NewRole { get; set; } = string.Empty;
    }
}
