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
        public string FullName { get; set; } = string.Empty;

        /// <summary>
        /// Role: Student, Tutor, Parent (mặc định là Student)
        /// </summary>
        public string Role { get; set; } = UserRole.Student;
    }
}
