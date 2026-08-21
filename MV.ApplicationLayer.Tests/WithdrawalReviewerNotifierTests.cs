using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using MV.ApplicationLayer.Helpers;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.Entities;
using MV.InfrastructureLayer.DBContext;
using Xunit;

namespace MV.ApplicationLayer.Tests;

public class WithdrawalReviewerNotifierTests
{
    [Theory]
    [InlineData(UserRole.Tutor, "Gia sư")]
    [InlineData(UserRole.Parent, "Phụ huynh")]
    [InlineData(UserRole.Student, "Học sinh")]
    [InlineData(UserRole.Staff, "Người dùng")]
    [InlineData(null, "Người dùng")]
    public void RoleLabel_MapsRequesterRoleToVietnamese(string? role, string expected)
    {
        Assert.Equal(expected, WithdrawalReviewerNotifier.RoleLabel(role));
    }

    [Fact]
    public void BuildMessage_NamesRequesterAndAmount()
    {
        var message = WithdrawalReviewerNotifier.BuildMessage("Nguyễn Văn A", UserRole.Tutor, 500000m);

        Assert.StartsWith("Gia sư Nguyễn Văn A vừa gửi yêu cầu rút ", message);
        Assert.Contains($"{500000m:N0}đ", message);
    }

    [Fact]
    public void BuildMessage_FallsBackWhenRequesterHasNoName()
    {
        var message = WithdrawalReviewerNotifier.BuildMessage("   ", UserRole.Parent, 100000m);

        Assert.Contains("Phụ huynh (chưa đặt tên)", message);
    }

    [Fact]
    public async Task NotifyNewRequestAsync_NotifiesAdminsAndPayoutStaffOnly()
    {
        await using var context = CreateContext();
        context.Users.AddRange(
            CreateUser("admin-1", "Quản trị viên 1", UserRole.Admin),
            CreateUser("admin-2", "Quản trị viên 2", UserRole.Admin),
            CreateUser("staff-payout", "Nhân viên tài chính", UserRole.Staff),
            CreateUser("staff-support", "Nhân viên hỗ trợ", UserRole.Staff),
            CreateUser("staff-unassigned", "Nhân viên chưa gán nhóm", UserRole.Staff),
            CreateUser("tutor-1", "Nguyễn Văn A", UserRole.Tutor));

        AddGroupWithStaff(context, "Tài chính", Permissions.PayoutView, "staff-payout");
        AddGroupWithStaff(context, "Hỗ trợ", Permissions.SupportView, "staff-support");
        context.StaffPermissionGroupAssignments.Add(new StaffPermissionGroupAssignment
        {
            StaffUserId = "staff-unassigned",
            PermissionGroupId = null,
            UpdatedBy = "admin-1",
            UpdatedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        var notifications = new RecordingNotificationService();
        var withdrawal = new Withdrawalrequest { Withdrawalid = 77, Userid = "tutor-1", Amount = 500000m };

        await WithdrawalReviewerNotifier.NotifyNewRequestAsync(
            context, notifications, NullLogger.Instance, withdrawal);

        Assert.Equal(
            new[] { "admin-1", "admin-2", "staff-payout" },
            notifications.Sent.Select(n => n.Userid).OrderBy(id => id, StringComparer.Ordinal));
        Assert.All(notifications.Sent, sent =>
        {
            Assert.Equal(WithdrawalReviewerNotifier.Title, sent.Title);
            Assert.Equal(NotificationType.WithdrawalRequestNew, sent.Type);
            Assert.Equal("77", sent.Referenceid);
            Assert.Contains("Gia sư Nguyễn Văn A", sent.Message);
        });
    }

    /// <summary>
    /// Nhóm quyền bị xoá mềm thì staff trong đó mất quyền vào trang payout — không được báo nữa,
    /// nếu không họ sẽ bấm vào thông báo rồi bị route guard chặn.
    /// </summary>
    [Fact]
    public async Task NotifyNewRequestAsync_SkipsStaffWhosePermissionGroupWasDeleted()
    {
        await using var context = CreateContext();
        context.Users.AddRange(
            CreateUser("staff-payout", "Nhân viên tài chính", UserRole.Staff),
            CreateUser("tutor-1", "Nguyễn Văn A", UserRole.Tutor));
        AddGroupWithStaff(context, "Tài chính", Permissions.PayoutView, "staff-payout", isDeleted: true);
        await context.SaveChangesAsync();

        var notifications = new RecordingNotificationService();

        await WithdrawalReviewerNotifier.NotifyNewRequestAsync(
            context,
            notifications,
            NullLogger.Instance,
            new Withdrawalrequest { Withdrawalid = 3, Userid = "tutor-1", Amount = 100000m });

        Assert.Empty(notifications.Sent);
    }

    [Fact]
    public async Task NotifyNewRequestAsync_NoReviewerAccount_SendsNothingAndDoesNotThrow()
    {
        await using var context = CreateContext();
        context.Users.Add(CreateUser("tutor-1", "Nguyễn Văn A", UserRole.Tutor));
        await context.SaveChangesAsync();

        var notifications = new RecordingNotificationService();

        await WithdrawalReviewerNotifier.NotifyNewRequestAsync(
            context,
            notifications,
            NullLogger.Instance,
            new Withdrawalrequest { Withdrawalid = 1, Userid = "tutor-1", Amount = 100000m });

        Assert.Empty(notifications.Sent);
    }

    /// <summary>
    /// Thông báo là best-effort — yêu cầu rút tiền đã commit rồi, lỗi gửi thông báo
    /// không được phép ném ngược lên người dùng.
    /// </summary>
    [Fact]
    public async Task NotifyNewRequestAsync_SwallowsNotificationFailure()
    {
        await using var context = CreateContext();
        context.Users.AddRange(
            CreateUser("admin-1", "Quản trị viên", UserRole.Admin),
            CreateUser("tutor-1", "Nguyễn Văn A", UserRole.Tutor));
        await context.SaveChangesAsync();

        var notifications = new RecordingNotificationService { ThrowOnSend = true };

        await WithdrawalReviewerNotifier.NotifyNewRequestAsync(
            context,
            notifications,
            NullLogger.Instance,
            new Withdrawalrequest { Withdrawalid = 2, Userid = "tutor-1", Amount = 100000m });
    }

    private static AgoraDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AgoraDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new NotifierTestDbContext(options);
    }

    private static void AddGroupWithStaff(
        AgoraDbContext context,
        string groupName,
        string permissionKey,
        string staffUserId,
        bool isDeleted = false)
    {
        var groupId = Guid.NewGuid();
        context.PermissionGroups.Add(new PermissionGroup
        {
            PermissionGroupId = groupId,
            Name = groupName,
            IsDeleted = isDeleted,
            CreatedBy = "admin-1",
            CreatedAt = DateTime.UtcNow,
            UpdatedBy = "admin-1",
            UpdatedAt = DateTime.UtcNow,
            Permissions = [new PermissionGroupPermission { PermissionGroupId = groupId, PermissionKey = permissionKey }]
        });
        context.StaffPermissionGroupAssignments.Add(new StaffPermissionGroupAssignment
        {
            StaffUserId = staffUserId,
            PermissionGroupId = groupId,
            UpdatedBy = "admin-1",
            UpdatedAt = DateTime.UtcNow
        });
    }

    private static User CreateUser(string id, string fullName, string role) => new()
    {
        Userid = id,
        Username = id,
        Password = "test-hash",
        Email = $"{id}@test.local",
        Fullname = fullName,
        Primaryrole = role,
        Status = 1,
        Createdat = DateTime.UtcNow
    };

    private sealed class NotifierTestDbContext(DbContextOptions<AgoraDbContext> options)
        : AgoraDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<QuestionBank>().Ignore(question => question.Embedding);
            modelBuilder.Entity<TutoraKbChunk>().Ignore(chunk => chunk.Embedding);
        }
    }

    /// <summary>Chỉ ghi lại batch được gửi; các thao tác khác không dùng trong test này.</summary>
    private sealed class RecordingNotificationService : INotificationService
    {
        public List<NotificationRequest> Sent { get; } = [];
        public bool ThrowOnSend { get; init; }

        public Task<StatusResponse> CreateNotificationsAsync(IEnumerable<NotificationRequest> requests)
        {
            if (ThrowOnSend) throw new InvalidOperationException("SignalR down");
            Sent.AddRange(requests);
            return Task.FromResult(new StatusResponse { Status = NotificationStatus.Success });
        }

        public Task<StatusResponse> CreateNotificationAsync(NotificationRequest request) => throw new NotSupportedException();
        public Task<NotificationResponse?> GetNotificationByIdAsync(int notificationId) => throw new NotSupportedException();
        public Task<IEnumerable<NotificationResponse>> GetNotificationsByUserIdAsync(string userId) => throw new NotSupportedException();
        public Task<IEnumerable<NotificationResponse>> GetUnreadNotificationsByUserIdAsync(string userId) => throw new NotSupportedException();
        public Task<int> GetUnreadCountByUserIdAsync(string userId) => throw new NotSupportedException();
        public Task<UnreadCountResponse> GetUnreadCountResponseByUserIdAsync(string userId) => throw new NotSupportedException();
        public Task<IEnumerable<NotificationResponse>> GetAllNotificationsAsync() => throw new NotSupportedException();
        public Task<StatusResponse> MarkAsReadAsync(int notificationId, string currentUserId) => throw new NotSupportedException();
        public Task<StatusResponse> MarkAllAsReadAsync(string userId) => throw new NotSupportedException();
        public Task<StatusResponse> MarkAsReadByTypeAsync(string userId, string type) => throw new NotSupportedException();
        public Task<StatusResponse> DeleteNotificationAsync(int notificationId, string currentUserId) => throw new NotSupportedException();
        public Task<StatusResponse> DeleteAllNotificationsByUserIdAsync(string userId) => throw new NotSupportedException();
        public Task<StatusResponse> DeleteOldNotificationsAsync(int daysOld) => throw new NotSupportedException();
    }
}
