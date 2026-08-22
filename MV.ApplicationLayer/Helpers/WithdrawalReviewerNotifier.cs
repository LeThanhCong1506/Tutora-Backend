using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MV.ApplicationLayer.Interfaces;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.Entities;

namespace MV.ApplicationLayer.Helpers;

/// <summary>
/// Báo cho người có quyền duyệt biết có yêu cầu rút tiền mới đang chờ xử lý.
///
/// Dùng chung cho cả hai luồng tạo yêu cầu — gia sư (TutorFinanceService) và phụ huynh/học sinh
/// (WalletService) — vì cả hai đều sinh Withdrawalrequest ở trạng thái pending_review và cùng chờ
/// một người xử lý.
/// </summary>
public static class WithdrawalReviewerNotifier
{
    public const string Title = "Yêu cầu rút tiền mới";

    /// <summary>
    /// Nội dung thông báo. Tách khỏi phần I/O để test được mà không cần DB.
    /// </summary>
    public static string BuildMessage(string? requesterName, string? requesterRole, decimal amount) =>
        $"{RoleLabel(requesterRole)} {DisplayName(requesterName)} vừa gửi yêu cầu rút {amount:N0}đ và đang chờ duyệt.";

    /// <summary>Nhãn tiếng Việt của role người yêu cầu, dùng trong nội dung thông báo.</summary>
    public static string RoleLabel(string? role) => role switch
    {
        UserRole.Tutor => "Gia sư",
        UserRole.Parent => "Phụ huynh",
        UserRole.Student => "Học sinh",
        _ => "Người dùng"
    };

    private static string DisplayName(string? fullName) =>
        string.IsNullOrWhiteSpace(fullName) ? "(chưa đặt tên)" : fullName.Trim();

    /// <summary>
    /// Những người mở được hàng đợi duyệt rút tiền: mọi Admin (RequirePermission cho Admin bypass
    /// toàn bộ) cộng với Staff đang được gán nhóm quyền có <see cref="Permissions.PayoutView"/>.
    ///
    /// Gate bằng PayoutView chứ không phải PayoutApprove vì đó đúng là tập người bấm vào thông báo
    /// thì mở được trang payout — approve/reject/transfer đều lấy PayoutView làm quyền phụ thuộc.
    /// </summary>
    public static Task<List<string>> GetReviewerIdsAsync(IAppDbContext context, CancellationToken ct = default) =>
        PermissionRecipients.ResolveAsync(context, Permissions.PayoutView, ct: ct);

    /// <summary>
    /// Gửi thông báo cho toàn bộ người duyệt. Best-effort: mọi lỗi chỉ ghi log warning, vì yêu cầu
    /// rút tiền đã commit thành công rồi — không được để việc gửi thông báo làm hỏng thao tác của
    /// người dùng. Gọi SAU khi transaction đã commit.
    /// </summary>
    public static async Task NotifyNewRequestAsync(
        IAppDbContext context,
        INotificationService notificationService,
        ILogger logger,
        Withdrawalrequest withdrawal,
        CancellationToken ct = default)
    {
        try
        {
            var requester = await context.Users
                .AsNoTracking()
                .Where(user => user.Userid == withdrawal.Userid)
                .Select(user => new { user.Fullname, user.Primaryrole })
                .FirstOrDefaultAsync(ct);

            var reviewerIds = await GetReviewerIdsAsync(context, ct);

            if (reviewerIds.Count == 0)
            {
                logger.LogWarning(
                    "No admin or payout staff to notify about new withdrawal {WithdrawalId}", withdrawal.Withdrawalid);
                return;
            }

            var message = BuildMessage(requester?.Fullname, requester?.Primaryrole, withdrawal.Amount ?? 0);

            await notificationService.CreateNotificationsAsync(reviewerIds.Select(reviewerId => new NotificationRequest
            {
                Userid = reviewerId,
                Title = Title,
                Message = message,
                Type = NotificationType.WithdrawalRequestNew,
                Referenceid = withdrawal.Withdrawalid.ToString()
            }));
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex, "Failed to notify reviewers about new withdrawal {WithdrawalId}", withdrawal.Withdrawalid);
        }
    }
}
