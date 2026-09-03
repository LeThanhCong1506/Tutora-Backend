using System.Text.Json;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;
using Xunit;

namespace MV.ApplicationLayer.Tests;

/// <summary>
/// Filter của trợ lý AI web là STATE HỘI THOẠI do tutora-ai sở hữu; .NET chỉ chuyển tiếp.
/// Bản cũ khai DTO 5 field nên khi tutora-ai thêm grade_level_id + lịch rảnh thì 4 field
/// đó bị vứt lặng lẽ: lượt sau bot mất tiêu chí lớp/lịch, trả lời vẫn trôi chảy nhưng lọc
/// SAI (không lỗi, không rỗng — dạng bug khó thấy nhất). Test khoá lại hành vi "không cắt".
/// </summary>
public class AssistantFiltersPassthroughTests
{
    // Đúng 9 field của TutorChatFilters (tutora-ai/app/models/schemas.py).
    private const string FullFiltersJson = """
    {
      "min_rate": 100000,
      "max_rate": 300000,
      "tutor_gender": "female",
      "subject_id": 1,
      "grade_level_id": 60,
      "desired_count": 2,
      "available_days": [6, 7],
      "available_from": "19:00",
      "available_to": "21:00"
    }
    """;

    [Fact]
    public void CurrentFilters_giu_nguyen_moi_field_khi_deserialize_request()
    {
        var body = $$"""{"message":"con ai khac khong","currentFilters":{{FullFiltersJson}}}""";

        var dto = JsonSerializer.Deserialize<AssistantRespondRequest>(
            body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        Assert.NotNull(dto.CurrentFilters);
        var f = dto.CurrentFilters!.Value;

        // 5 field bản cũ đã có.
        Assert.Equal(100000, f.GetProperty("min_rate").GetDouble());
        Assert.Equal("female", f.GetProperty("tutor_gender").GetString());
        Assert.Equal(1, f.GetProperty("subject_id").GetInt32());
        Assert.Equal(2, f.GetProperty("desired_count").GetInt32());

        // 4 field bản cũ LÀM RƠI — chính là nguyên nhân mất lớp + lịch giữa các lượt.
        Assert.Equal(60, f.GetProperty("grade_level_id").GetInt32());
        Assert.Equal("19:00", f.GetProperty("available_from").GetString());
        Assert.Equal("21:00", f.GetProperty("available_to").GetString());
        Assert.Equal(new[] { 6, 7 },
            f.GetProperty("available_days").EnumerateArray().Select(x => x.GetInt32()).ToArray());
    }

    [Fact]
    public void Filters_tra_ve_FE_giu_nguyen_moi_field()
    {
        // Mô phỏng ParseAssistantResponse: đọc từ JsonDocument rồi Clone() ra ngoài.
        JsonElement cloned;
        using (var doc = JsonDocument.Parse($$"""{"filters":{{FullFiltersJson}}}"""))
            cloned = doc.RootElement.GetProperty("filters").Clone();

        var res = new AssistantRespondResponse { Filters = cloned };

        // Clone() phải sống sót sau khi JsonDocument bị dispose — thiếu Clone là đọc bộ nhớ đã huỷ.
        // camelCase: khớp cấu hình mặc định của ASP.NET Core, đúng thứ FE nhận được.
        var json = JsonSerializer.Serialize(res,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        using var round = JsonDocument.Parse(json);
        var f = round.RootElement.GetProperty("filters");

        Assert.Equal(60, f.GetProperty("grade_level_id").GetInt32());
        Assert.Equal(2, f.GetProperty("available_days").GetArrayLength());
        Assert.Equal("19:00", f.GetProperty("available_from").GetString());
    }

    [Fact]
    public void Filter_moi_cua_tutora_ai_khong_can_sua_dotnet()
    {
        // Hạng mục kế tiếp: cho tiêu chí mềm ("mất gốc", "cần cô kiên nhẫn") tích luỹ qua
        // các lượt. Với passthrough, thêm field mới KHÔNG phải đụng .NET nữa.
        var body = """{"message":"x","currentFilters":{"subject_id":1,"preferences":"con mat goc, can co kien nhan"}}""";

        var dto = JsonSerializer.Deserialize<AssistantRespondRequest>(
            body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        Assert.Equal("con mat goc, can co kien nhan",
            dto.CurrentFilters!.Value.GetProperty("preferences").GetString());
    }
}
