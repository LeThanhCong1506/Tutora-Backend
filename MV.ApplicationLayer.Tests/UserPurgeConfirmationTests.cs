using MV.ApplicationLayer.Helpers;
using Xunit;

namespace MV.ApplicationLayer.Tests;

/// <summary>
/// The typed sentence is the last gate before an irreversible delete, so it has to reject the
/// near-misses it exists to catch while still accepting what an operator would realistically type.
/// </summary>
public class UserPurgeConfirmationTests
{
    private const string Admin = "Dương Thành Phát";
    private const string Target = "Nguyễn Văn A";

    [Fact]
    public void Build_NamesBothTheOperatorAndTheTarget()
    {
        var phrase = UserPurgeConfirmation.Build(Admin, Target);

        Assert.Equal(
            "Admin Dương Thành Phát đồng ý xóa vĩnh viễn dữ liệu của người dùng Nguyễn Văn A",
            phrase);
    }

    [Fact]
    public void Matches_AcceptsTheExactSentence()
    {
        var phrase = UserPurgeConfirmation.Build(Admin, Target);

        Assert.True(UserPurgeConfirmation.Matches(phrase, phrase));
    }

    [Theory]
    [InlineData("  Admin Dương Thành Phát đồng ý xóa vĩnh viễn dữ liệu của người dùng Nguyễn Văn A  ")]
    [InlineData("Admin Dương Thành Phát  đồng ý  xóa vĩnh viễn dữ liệu của người dùng Nguyễn Văn A")]
    [InlineData("admin dương thành phát đồng ý xóa vĩnh viễn dữ liệu của người dùng nguyễn văn a")]
    public void Matches_ForgivesSpacingAndCasing(string typed)
    {
        // A correct paste that picked up a stray space or a leading capital is still the operator
        // having read and reproduced the sentence.
        Assert.True(UserPurgeConfirmation.Matches(UserPurgeConfirmation.Build(Admin, Target), typed));
    }

    [Fact]
    public void Matches_RejectsAPhraseNamingADifferentTarget()
    {
        // The whole point: a dialog opened on the wrong row, or a sentence reused from the last
        // deletion, must not go through.
        var expected = UserPurgeConfirmation.Build(Admin, Target);
        var otherTarget = UserPurgeConfirmation.Build(Admin, "Trần Thị B");

        Assert.False(UserPurgeConfirmation.Matches(expected, otherTarget));
    }

    [Fact]
    public void Matches_RejectsAPhraseNamingADifferentOperator()
    {
        var expected = UserPurgeConfirmation.Build(Admin, Target);
        var otherAdmin = UserPurgeConfirmation.Build("Lê Quản Trị", Target);

        Assert.False(UserPurgeConfirmation.Matches(expected, otherAdmin));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("xóa")]
    [InlineData("Admin Dương Thành Phát đồng ý xóa vĩnh viễn dữ liệu của người dùng")]
    public void Matches_RejectsBlankAndTruncatedInput(string? typed)
    {
        Assert.False(UserPurgeConfirmation.Matches(UserPurgeConfirmation.Build(Admin, Target), typed));
    }

    [Fact]
    public void Matches_RejectsDiacriticStrippedText()
    {
        // Typing without diacritics is a different sentence, not a typo — accepting it would weaken
        // the gate to roughly "type some Vietnamese-looking words".
        Assert.False(UserPurgeConfirmation.Matches(
            UserPurgeConfirmation.Build(Admin, Target),
            "Admin Duong Thanh Phat dong y xoa vinh vien du lieu cua nguoi dung Nguyen Van A"));
    }

    [Fact]
    public void Build_FallsBackWhenANameIsMissing()
    {
        // A staff account created without a display name must still produce a usable sentence
        // rather than "Admin  đồng ý ... người dùng ".
        var phrase = UserPurgeConfirmation.Build(null, null);

        Assert.Equal("Admin Quản trị viên đồng ý xóa vĩnh viễn dữ liệu của người dùng này", phrase);
        Assert.True(UserPurgeConfirmation.Matches(phrase, phrase));
    }
}
