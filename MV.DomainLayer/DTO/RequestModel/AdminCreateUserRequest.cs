using System.ComponentModel.DataAnnotations;

namespace MV.DomainLayer.DTO.RequestModel
{
    /// <summary>
    /// Payload cho Admin/Staff tạo tài khoản khách hàng (POST /api/admin/users).
    /// Chỉ áp dụng cho các role khách hàng: Student, Parent, Tutor. Tài khoản nội
    /// bộ (Staff/Admin) có luồng riêng (POST /api/staffs). Vì do Admin tạo và bảo
    /// lãnh, tài khoản được đánh dấu đã xác thực SĐT (Isphoneverified = true) để
    /// đăng nhập được ngay mà không cần OTP.
    /// </summary>
    public class AdminCreateUserRequest
    {
        [Required(ErrorMessage = "Họ tên không được để trống.")]
        [StringLength(100, ErrorMessage = "Họ tên không được vượt quá 100 ký tự.")]
        public string Fullname { get; set; } = null!;

        // Email tùy chọn — khách hàng đăng nhập bằng SĐT. Nếu có thì phải hợp lệ & duy nhất.
        [EmailAddress(ErrorMessage = "Email không đúng định dạng.")]
        public string? Email { get; set; }

        // SĐT bắt buộc: cổng đăng nhập của khách hàng dựa trên SĐT đã xác thực.
        [Required(ErrorMessage = "Số điện thoại không được để trống.")]
        [Phone(ErrorMessage = "Số điện thoại không đúng định dạng.")]
        public string Phone { get; set; } = null!;

        [Required(ErrorMessage = "Mật khẩu không được để trống.")]
        [RegularExpression(
            @"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[@$!%*?&])[A-Za-z\d@$!%*?&]{8,}$",
            ErrorMessage = "Mật khẩu tối thiểu 8 ký tự, gồm chữ hoa, chữ thường, số và ký tự đặc biệt.")]
        public string Password { get; set; } = null!;

        /// <summary>
        /// Role cần gán. Chỉ chấp nhận: "Student", "Parent", "Tutor".
        /// </summary>
        [Required(ErrorMessage = "Vai trò không được để trống.")]
        public string Role { get; set; } = null!;
    }
}
