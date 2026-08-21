using Microsoft.EntityFrameworkCore;
using MV.ApplicationLayer.Interfaces;
using MV.DomainLayer.Constants;

namespace MV.ApplicationLayer.Helpers;

/// <summary>
/// Giải ra danh sách người trong CMS cần được báo khi có việc mới thuộc một nghiệp vụ.
///
/// Trước đây mỗi luồng tự query `Primaryrole == Admin`, nên Staff dù đã được gán nhóm quyền
/// tương ứng vẫn không hề nhận được thông báo — họ chỉ biết có việc khi tự mở CMS ra xem.
/// Dùng helper này thay cho việc query Admin trực tiếp.
/// </summary>
public static class PermissionRecipients
{
    /// <summary>
    /// Những người mở được màn hình xử lý ứng với <paramref name="permissionKey"/>:
    /// mọi Admin (PermissionRequirementHandler cho Admin bypass toàn bộ) cộng với Staff đang được
    /// gán nhóm quyền còn hiệu lực có chứa key đó.
    ///
    /// Luôn gate bằng quyền `*.view` — đó đúng là tập người bấm vào thông báo thì mở được trang,
    /// và mọi quyền hành động (approve/reject/resolve/decide) đều lấy `*.view` làm quyền phụ thuộc.
    /// </summary>
    /// <param name="excludeUserId">
    /// Người vừa gây ra sự kiện — không tự báo cho chính mình (vd Admin trả lời hội thoại hỗ trợ).
    /// </param>
    public static async Task<List<string>> ResolveAsync(
        IAppDbContext context,
        string permissionKey,
        string? excludeUserId = null,
        CancellationToken ct = default)
    {
        var adminIds = await context.Users
            .AsNoTracking()
            .Where(user => user.Primaryrole == UserRole.Admin)
            .Select(user => user.Userid)
            .ToListAsync(ct);

        var staffIds = await context.StaffPermissionGroupAssignments
            .AsNoTracking()
            .Where(assignment => assignment.StaffUser.Primaryrole == UserRole.Staff
                && assignment.PermissionGroupId != null
                && assignment.PermissionGroup != null
                && !assignment.PermissionGroup.IsDeleted
                && assignment.PermissionGroup.Permissions.Any(p => p.PermissionKey == permissionKey))
            .Select(assignment => assignment.StaffUserId)
            .ToListAsync(ct);

        return adminIds
            .Union(staffIds, StringComparer.Ordinal)
            .Where(id => !string.IsNullOrWhiteSpace(id) && id != excludeUserId)
            .ToList();
    }
}
