namespace MV.DomainLayer.Helpers;

/// <summary>
/// Tính tuổi từ ngày sinh. Dùng cho gate độ tuổi.
/// </summary>
public static class AgeHelper
{
    /// <summary>
    /// Độ tuổi tối thiểu để học sinh tự đăng ký được phép tự đặt lịch.
    /// </summary>
    public const int MinSelfBookingAge = 16;

    /// <summary>
    /// Số tuổi (năm) tính đến hôm nay theo ngày sinh.
    /// </summary>
    public static int CalculateAge(DateOnly birthdate)
        => CalculateAge(birthdate, DateOnly.FromDateTime(TimeZoneHelper.UtcNow));

    /// <summary>
    /// Số tuổi (năm) tính đến một mốc ngày cho trước.
    /// </summary>
    public static int CalculateAge(DateOnly birthdate, DateOnly asOf)
    {
        var age = asOf.Year - birthdate.Year;
        if (birthdate > asOf.AddYears(-age)) age--;
        return age < 0 ? 0 : age;
    }

    /// <summary>
    /// Đã đủ tuổi tối thiểu để tự đặt lịch chưa.
    /// </summary>
    public static bool IsOldEnoughToSelfBook(DateOnly? birthdate)
        => birthdate.HasValue && CalculateAge(birthdate.Value) >= MinSelfBookingAge;
}
