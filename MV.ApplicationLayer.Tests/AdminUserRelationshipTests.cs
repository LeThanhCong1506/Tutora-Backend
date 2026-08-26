using Microsoft.EntityFrameworkCore;
using MV.ApplicationLayer.Services;
using MV.DomainLayer.Constants;
using MV.DomainLayer.Entities;
using MV.InfrastructureLayer.DBContext;
using Xunit;

namespace MV.ApplicationLayer.Tests;

public class AdminUserRelationshipTests
{
    [Fact]
    public async Task ParentDetail_ReturnsLinkedAccountsAndLegacyProfiles()
    {
        await using var context = CreateContext();
        var parent = CreateUser("parent-1", "Nguyễn Phụ Huynh", UserRole.Parent);
        var student = CreateUser("student-1", "Nguyễn Học Sinh", UserRole.Student);

        context.Users.AddRange(parent, student);
        context.Studentprofiles.AddRange(
            new Studentprofile
            {
                Studentid = "profile-linked",
                Parentid = parent.Userid,
                Linkeduserid = student.Userid,
                Fullname = "Tên cũ trong hồ sơ",
                Createdat = DateTime.UtcNow
            },
            new Studentprofile
            {
                Studentid = "profile-legacy",
                Parentid = parent.Userid,
                Fullname = "Học sinh chưa có tài khoản",
                Avatarurl = "legacy-avatar.png",
                Createdat = DateTime.UtcNow.AddMinutes(1)
            },
            new Studentprofile
            {
                Studentid = "profile-deleted",
                Parentid = parent.Userid,
                Fullname = "Hồ sơ đã xóa",
                Deletedat = DateTime.UtcNow
            });
        await context.SaveChangesAsync();

        var detail = await CreateService(context).AdminGetUserDetailAsync(parent.Userid);

        Assert.Equal(parent.Userid, detail.User.Userid);
        Assert.Null(detail.Relationships.Parent);
        Assert.Equal(2, detail.Relationships.Students.Count);

        var linked = Assert.Single(detail.Relationships.Students, item => item.UserId == student.Userid);
        Assert.Equal(student.Fullname, linked.FullName);
        Assert.True(linked.HasAccount);

        var legacy = Assert.Single(detail.Relationships.Students, item => item.StudentProfileId == "profile-legacy");
        Assert.Null(legacy.UserId);
        Assert.Equal("Học sinh chưa có tài khoản", legacy.FullName);
        Assert.Equal("legacy-avatar.png", legacy.AvatarUrl);
        Assert.False(legacy.HasAccount);
    }

    [Fact]
    public async Task StudentDetail_ReturnsParentFromLinkedProfile()
    {
        await using var context = CreateContext();
        var parent = CreateUser("parent-2", "Trần Phụ Huynh", UserRole.Parent);
        var student = CreateUser("student-2", "Trần Học Sinh", UserRole.Student);

        context.Users.AddRange(parent, student);
        context.Studentprofiles.Add(new Studentprofile
        {
            Studentid = "profile-2",
            Parentid = parent.Userid,
            Linkeduserid = student.Userid,
            Fullname = student.Fullname,
            Createdat = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        var detail = await CreateService(context).AdminGetUserDetailAsync(student.Userid);

        var linkedParent = Assert.IsType<MV.DomainLayer.DTO.ResponseModel.Admin.AdminLinkedUserResponse>(
            detail.Relationships.Parent);
        Assert.Equal(parent.Userid, linkedParent.UserId);
        Assert.Equal(parent.Fullname, linkedParent.FullName);
        Assert.Equal("profile-2", linkedParent.StudentProfileId);
        Assert.True(linkedParent.HasAccount);
        Assert.Empty(detail.Relationships.Students);
    }

    [Fact]
    public async Task StudentDetail_SupportsLegacyProfileWhoseIdMatchesUserId()
    {
        await using var context = CreateContext();
        var parent = CreateUser("parent-3", "Lê Phụ Huynh", UserRole.Parent);
        var student = CreateUser("student-legacy", "Lê Học Sinh", UserRole.Student);

        context.Users.AddRange(parent, student);
        context.Studentprofiles.Add(new Studentprofile
        {
            Studentid = student.Userid,
            Parentid = parent.Userid,
            Linkeduserid = null,
            Fullname = student.Fullname,
            Createdat = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        var detail = await CreateService(context).AdminGetUserDetailAsync(student.Userid);

        Assert.NotNull(detail.Relationships.Parent);
        Assert.Equal(parent.Userid, detail.Relationships.Parent.UserId);
    }

    private static AgoraDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AgoraDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new RelationshipTestDbContext(options);
    }

    private static UserService CreateService(AgoraDbContext context) =>
        new(null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, context, null!, null!,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<UserService>.Instance);

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

    private sealed class RelationshipTestDbContext(DbContextOptions<AgoraDbContext> options)
        : AgoraDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<QuestionBank>().Ignore(question => question.Embedding);
            modelBuilder.Entity<TutoraKbChunk>().Ignore(chunk => chunk.Embedding);
        }
    }
}
