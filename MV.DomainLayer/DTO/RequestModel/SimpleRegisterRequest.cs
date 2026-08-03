using System.ComponentModel.DataAnnotations;
using MV.DomainLayer.Constants;

namespace MV.DomainLayer.DTO.RequestModel
{
    /// <summary>
    /// Request đơn giản cho register (không qua Supabase)
    /// </summary>
    public class SimpleRegisterRequest
    {
        /// <summary>
        /// Email
        /// </summary>
        public string? Email { get; set; }

        /// <summary>
        /// Số điện thoại (tùy chọn)
        /// </summary>
        public string? Phone { get; set; }

        /// <summary>
        /// Mật khẩu
        /// </summary>
        public string Password { get; set; } = string.Empty;

        /// <summary>
        /// Họ tên đầy đủ
        /// </summary>
        [Required(ErrorMessage = "Họ tên không được để trống.")]
        [StringLength(100, MinimumLength = 2, ErrorMessage = "Họ tên phải từ 2 đến 100 ký tự.")]
        public string FullName { get; set; } = string.Empty;

        /// <summary>
        /// Role: Student, Tutor, Parent (mặc định là Student)
        /// </summary>
        public string Role { get; set; } = UserRole.Student;

        /// <summary>
        /// SĐT phụ huynh để nhận ZNS theo dõi.
        /// </summary>
        public string? ParentPhone { get; set; }
    }
}
