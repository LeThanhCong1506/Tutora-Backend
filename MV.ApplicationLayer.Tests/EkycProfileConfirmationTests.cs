using System.Text.Json;
using MV.DomainLayer.Entities;
using MV.DomainLayer.Enums;
using MV.DomainLayer.Helpers;
using Xunit;

namespace MV.ApplicationLayer.Tests;

/// <summary>
/// Quét CCCD chỉ lưu dữ liệu OCR; hồ sơ (họ tên, ngày sinh, giới tính, địa chỉ thường trú)
/// chỉ đổi khi gia sư bấm xác nhận. Các test dưới đây khoá đúng ranh giới đó.
/// </summary>
public class EkycProfileConfirmationTests
{
    private static User MakeUser() => new()
    {
        Userid = "u-1",
        Fullname = "Phat Duong",
        Birthdate = new DateOnly(1999, 1, 1),
        Gender = Gender.Female,
        Address = "Địa chỉ tự nhập cũ",
    };

    private static EkycProfileSync.OcrProfileData MakeOcr() => new()
    {
        Name = "DƯƠNG THÀNH PHÁT",
        Dob = "04/10/2001",
        Sex = "NAM",
        Home = "Quảng Nam",
        Address = "123 Lê Lợi, Phường 1, Quận 3, TP. Hồ Chí Minh",
    };

    [Fact]
    public void Preview_ListsChangesWithoutTouchingTheProfile()
    {
        var user = MakeUser();

        var changes = EkycProfileSync.Preview(user, MakeOcr());

        // Bước quét KHÔNG được ghi gì vào hồ sơ.
        Assert.Equal("Phat Duong", user.Fullname);
        Assert.Equal(new DateOnly(1999, 1, 1), user.Birthdate);
        Assert.Equal(Gender.Female, user.Gender);
        Assert.Equal("Địa chỉ tự nhập cũ", user.Address);

        Assert.Equal(
            new[] { "fullName", "dateOfBirth", "gender", "address" },
            changes.Select(c => c.Field).ToArray());

        var name = changes.Single(c => c.Field == "fullName");
        Assert.Equal("Họ và tên", name.Label);
        Assert.Equal("Phat Duong", name.CurrentValue);
        Assert.Equal("DƯƠNG THÀNH PHÁT", name.NewValue);

        var dob = changes.Single(c => c.Field == "dateOfBirth");
        Assert.Equal("01/01/1999", dob.CurrentValue);
        Assert.Equal("04/10/2001", dob.NewValue);

        var gender = changes.Single(c => c.Field == "gender");
        Assert.Equal("Nữ", gender.CurrentValue);
        Assert.Equal("Nam", gender.NewValue);
    }

    [Fact]
    public void Apply_WritesEveryFieldAndIsIdempotent()
    {
        var user = MakeUser();

        var applied = EkycProfileSync.Apply(user, MakeOcr());

        Assert.Equal(4, applied.Count);
        Assert.Equal("DƯƠNG THÀNH PHÁT", user.Fullname);
        Assert.Equal(new DateOnly(2001, 10, 4), user.Birthdate);
        Assert.Equal(Gender.Male, user.Gender);
        Assert.Equal("123 Lê Lợi, Phường 1, Quận 3, TP. Hồ Chí Minh", user.Address);

        // Xác nhận lần hai (bấm nhầm 2 lần, hoặc gọi lại API) không được báo đổi gì nữa.
        Assert.Empty(EkycProfileSync.Apply(user, MakeOcr()));
        Assert.Empty(EkycProfileSync.Preview(user, MakeOcr()));
    }

    [Fact]
    public void Preview_IgnoresFieldsThatOcrCouldNotRead()
    {
        var user = MakeUser();

        // OCR trả rỗng ở vài trường (ảnh mờ một góc) — không được xoá trắng hồ sơ đang có.
        var changes = EkycProfileSync.Preview(user, new EkycProfileSync.OcrProfileData
        {
            Name = "DƯƠNG THÀNH PHÁT",
            Dob = null,
            Sex = "",
            Address = "   ",
        });

        Assert.Equal(new[] { "fullName" }, changes.Select(c => c.Field).ToArray());
    }

    [Fact]
    public void Preview_SkipsDobThatDoesNotMatchCccdFormat()
    {
        var user = MakeUser();

        var changes = EkycProfileSync.Preview(user, new EkycProfileSync.OcrProfileData { Dob = "2001-10-04" });

        Assert.Empty(changes);
    }

    /// <summary>
    /// Contract giữa bước quét và bước xác nhận: EkycService bọc kết quả OCR trong
    /// { OcrResult: { id, name, dob, sex, home, address }, VerifiedAt } rồi mã hoá vào
    /// ekyc_raw_data. Nếu đổi tên khoá ở một bên mà quên bên kia thì màn hình xác nhận
    /// sẽ trống và bấm xác nhận sẽ không ghi được gì — nên khoá lại bằng test.
    /// </summary>
    [Fact]
    public void ParseStoredRawData_ReadsTheShapeEkycServiceWrites()
    {
        var stored = JsonSerializer.Serialize(new
        {
            OcrResult = new
            {
                id = "079201000123",
                name = "DƯƠNG THÀNH PHÁT",
                dob = "04/10/2001",
                sex = "NAM",
                home = "Quảng Nam",
                address = "123 Lê Lợi, Phường 1, Quận 3, TP. Hồ Chí Minh",
            },
            VerifiedAt = DateTime.UtcNow.ToString("o"),
        });

        var parsed = EkycProfileSync.ParseStoredRawData(stored);

        Assert.NotNull(parsed);
        Assert.Equal("DƯƠNG THÀNH PHÁT", parsed!.Name);
        Assert.Equal("04/10/2001", parsed.Dob);
        Assert.Equal("NAM", parsed.Sex);
        Assert.Equal("Quảng Nam", parsed.Home);
        Assert.Equal("123 Lê Lợi, Phường 1, Quận 3, TP. Hồ Chí Minh", parsed.Address);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{ not json")]
    public void ParseStoredRawData_ReturnsNullWhenThereIsNothingUsable(string? raw)
    {
        Assert.Null(EkycProfileSync.ParseStoredRawData(raw));
    }
}
