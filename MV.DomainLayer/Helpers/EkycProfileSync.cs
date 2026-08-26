using System.Globalization;
using System.Text.Json;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.Entities;

namespace MV.DomainLayer.Helpers
{
    /// <summary>
    /// So khớp và ghi dữ liệu định danh từ CCCD (OCR) vào hồ sơ người dùng.
    ///
    /// Tách ra khỏi EkycService vì có HAI nơi cần cùng một logic so sánh và KHÔNG được lệch nhau:
    ///   • lúc gia sư bấm xác nhận (EkycService/TutorService) — ghi thật;
    ///   • lúc build tiến trình hồ sơ (TutorVerificationService.Progress) — chỉ xem trước để biết
    ///     còn thay đổi nào chưa được xác nhận hay không.
    /// Nếu hai nơi so sánh khác nhau thì banner "chưa xác nhận" sẽ không bao giờ tắt (hoặc tắt sai).
    /// </summary>
    public static class EkycProfileSync
    {
        /// <summary>Các trường CCCD dùng để đồng bộ hồ sơ. Quê quán KHÔNG có ở đây — chỉ nằm trong ekyc_raw_data.</summary>
        public sealed class OcrProfileData
        {
            public string? Name { get; init; }
            public string? Dob { get; init; }
            public string? Sex { get; init; }
            public string? Address { get; init; }
            public string? Home { get; init; }
        }

        /// <summary>
        /// Bóc `OcrResult` trong chuỗi JSON đã giải mã của cột ekyc_raw_data.
        /// Trả null khi chưa quét CCCD hoặc dữ liệu hỏng — caller tự quyết định báo lỗi hay bỏ qua.
        /// </summary>
        public static OcrProfileData? ParseStoredRawData(string? decryptedRawData)
        {
            if (string.IsNullOrWhiteSpace(decryptedRawData)) return null;

            try
            {
                using var doc = JsonDocument.Parse(decryptedRawData);
                var root = doc.RootElement;
                // Dữ liệu được lưu dạng bọc { OcrResult: {...}, VerifiedAt }; vẫn chấp nhận dạng phẳng.
                var ocr = root.TryGetProperty("OcrResult", out var wrapped) ? wrapped : root;

                string? Read(string name) =>
                    ocr.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                        ? value.GetString()
                        : null;

                return new OcrProfileData
                {
                    Name = Read("name"),
                    Dob = Read("dob"),
                    Sex = Read("sex"),
                    Address = Read("address"),
                    Home = Read("home"),
                };
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>Ngày sinh trên CCCD ("dd/MM/yyyy") → DateOnly. Null khi không parse được.</summary>
        public static DateOnly? ParseDob(string? dob) =>
            !string.IsNullOrWhiteSpace(dob) &&
            DateOnly.TryParseExact(dob, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
                ? parsed
                : null;

        /// <summary>
        /// Liệt kê các trường hồ sơ sẽ thay đổi nếu áp dụng dữ liệu CCCD. KHÔNG đụng vào
        /// <paramref name="user"/> — dùng cho màn hình xác nhận và cho cờ "còn chờ xác nhận".
        /// </summary>
        public static List<EkycProfileFieldChange> Preview(User user, OcrProfileData data) =>
            Sync(user, data, apply: false);

        /// <summary>
        /// Ghi dữ liệu CCCD vào hồ sơ và trả về đúng những trường đã đổi. Chỉ gọi sau khi
        /// người dùng đã xác nhận (hoặc ở các luồng cố ý auto-fill, vd học sinh).
        /// </summary>
        public static List<EkycProfileFieldChange> Apply(User user, OcrProfileData data) =>
            Sync(user, data, apply: true);

        // CCCD là nguồn định danh chuẩn: họ tên hiển thị từ Zalo/Google có thể là nickname,
        // nên các trường dưới đây được GHI ĐÈ chứ không chỉ điền khi trống.
        private static List<EkycProfileFieldChange> Sync(User user, OcrProfileData data, bool apply)
        {
            var changes = new List<EkycProfileFieldChange>();

            if (!string.IsNullOrWhiteSpace(data.Name) &&
                !string.Equals(user.Fullname, data.Name, StringComparison.Ordinal))
            {
                changes.Add(new EkycProfileFieldChange
                {
                    Field = "fullName",
                    Label = "Họ và tên",
                    CurrentValue = user.Fullname,
                    NewValue = data.Name,
                });
                if (apply) user.Fullname = data.Name;
            }

            var dob = ParseDob(data.Dob);
            if (dob != null && user.Birthdate != dob)
            {
                changes.Add(new EkycProfileFieldChange
                {
                    Field = "dateOfBirth",
                    Label = "Ngày sinh",
                    CurrentValue = user.Birthdate?.ToString("dd/MM/yyyy"),
                    NewValue = dob.Value.ToString("dd/MM/yyyy"),
                });
                if (apply) user.Birthdate = dob;
            }

            var gender = GenderHelper.FromEkycSex(data.Sex);
            if (gender != null && user.Gender != gender)
            {
                changes.Add(new EkycProfileFieldChange
                {
                    Field = "gender",
                    Label = "Giới tính",
                    CurrentValue = DescribeGender(user.Gender),
                    NewValue = DescribeGender(gender),
                });
                if (apply) user.Gender = gender;
            }

            // Địa chỉ THƯỜNG TRÚ của tài khoản — khác KHU VỰC DẠY (tutor_profiles.teaching_area_*),
            // gia sư có thể thường trú Hà Nội mà dạy TP.HCM nên khu vực dạy không bị đụng tới ở đây.
            if (!string.IsNullOrWhiteSpace(data.Address) &&
                !string.Equals(user.Address, data.Address, StringComparison.Ordinal))
            {
                changes.Add(new EkycProfileFieldChange
                {
                    Field = "address",
                    Label = "Địa chỉ thường trú",
                    CurrentValue = user.Address,
                    NewValue = data.Address,
                });
                if (apply) user.Address = data.Address;
            }

            return changes;
        }

        /// <summary>Giới tính → chuỗi hiển thị tiếng Việt (dùng chung cho preview và response).</summary>
        public static string? DescribeGender(Enums.Gender? gender) => gender switch
        {
            Enums.Gender.Male => "Nam",
            Enums.Gender.Female => "Nữ",
            Enums.Gender.Other => "Khác",
            _ => null,
        };
    }
}
