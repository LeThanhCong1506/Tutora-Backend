namespace MV.DomainLayer.Constants;

/// <summary>
/// User role constants. Values must match the `primaryrole` column in the `users` table (PascalCase).
/// </summary>
public static class UserRole
{
    public const string Admin   = "Admin";
    public const string Tutor   = "Tutor";
    public const string Parent  = "Parent";
    public const string Student = "Student";
    public const string Staff   = "Staff";
    public const string User    = "User";

    // Combined constants for [Authorize(Roles = ...)] attributes
    public const string ParentOrStudent         = "Parent,Student";
    public const string ParentOrTutor           = "Parent,Tutor";
    public const string ParentOrStudentOrTutor  = "Parent,Student,Tutor";
    public const string AdminOrStaff            = "Admin,Staff";
    public const string ParentOrStudentOrAdmin  = "Parent,Student,Admin";
    public const string TutorOrAdmin            = "Tutor,Admin";

    /// <summary>
    /// Các role hợp lệ mà Admin được phép gán cho user qua API.
    /// Role "Admin" bị loại trừ — chỉ set trực tiếp qua DB.
    /// </summary>
    public static readonly IReadOnlySet<string> AssignableByAdmin = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Parent,
        Student,
        Tutor,
        Staff
    };

    /// <summary>
    /// Các role người dùng được phép tự chọn khi tự đăng ký.
    /// </summary>
    public static readonly IReadOnlySet<string> SelfRegisterable = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Parent,
        Student,
        Tutor
    };

    /// <summary>
    /// Tài khoản nội bộ (Admin cấp, không tự đăng ký). Các cổng onboarding
    /// dành cho khách hàng — bắt buộc có SĐT + xác thực OTP trước khi nhận
    /// token — không áp dụng cho nhóm này: họ xác thực bằng email + mật khẩu.
    /// </summary>
    public static bool IsInternal(string? role) =>
        string.Equals(role, Admin, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(role, Staff, StringComparison.OrdinalIgnoreCase);
}
