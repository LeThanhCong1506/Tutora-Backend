using System.ComponentModel.DataAnnotations;

namespace MV.DomainLayer.DTO.RequestModel
{
    /// <summary>
    /// Payload cho Admin tạo tài khoản nhân viên (POST /api/staffs).
    /// Chỉ gồm các trường tối thiểu để nhân viên đăng nhập được; thông tin cá
    /// nhân còn lại (ngày sinh, giới tính, địa chỉ, avatar) nhân viên tự cập
    /// nhật qua PUT /api/users/{id}. Phone là ngoại lệ: giữ ở đây (tùy chọn)
    /// vì UpdateUserRequest không hỗ trợ đổi số điện thoại. Username cũng tùy
    /// chọn — nhân viên đăng nhập bằng email; username chỉ là handle phụ.
    /// </summary>
    public class CreateStaffRequest
    {
        [StringLength(50, MinimumLength = 3,
            ErrorMessage = "Username must contain between 3 and 50 characters.")]
        public string? Username { get; set; }

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

        [Phone(ErrorMessage = "Phone number is not in a valid format.")]
        public string? Phone { get; set; }
    }
}
