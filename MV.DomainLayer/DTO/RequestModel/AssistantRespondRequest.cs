using System.Text.Json;

namespace MV.DomainLayer.DTO.RequestModel;

/// <summary>
/// Request FE gửi tới .NET cho trợ lý AI web (gợi ý gia sư / hỏi hệ thống / từ chối lạc đề).
/// PUBLIC — ai cũng chat được:
///   • Anonymous: FE tự giữ history (localStorage), gửi kèm mỗi lượt. .NET không lưu DB.
///   • Authed: .NET tự dựng history từ DB theo userId + lưu lại (History field bị bỏ qua).
/// </summary>
public class AssistantRespondRequest
{
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Lịch sử hội thoại
    /// Mỗi phần tử: { role: "user"|"assistant", content }.
    /// </summary>
    public List<AssistantHistoryItem> History { get; set; } = new();

    /// <summary>Ngữ cảnh tìm gia sư (môn/lớp/khu vực...) FE tích luỹ — forward nguyên sang AI.</summary>
    public AssistantContextDto? Context { get; set; }

    /// <summary>
    /// Filter đã tích luỹ qua các lượt (môn/lớp/giá/giới tính/lịch rảnh...) — FE giữ hộ giữa
    /// các lượt, .NET CHUYỂN NGUYÊN KHỐI sang tutora-ai.
    ///
    /// Cố tình để JsonElement thay vì DTO có field: đây là STATE HỘI THOẠI do tutora-ai sở
    /// hữu, .NET không đọc field nào để làm nghiệp vụ — chỉ chuyển qua chuyển lại. Bản cũ
    /// khai 5 field cụ thể nên khi tutora-ai thêm grade_level_id + lịch rảnh
    /// (available_days/from/to) thì 4 field đó bị vứt lặng lẽ khi deserialize: lượt sau bot
    /// mất tiêu chí lớp và lịch, vẫn trả lời trôi chảy nhưng lọc sai. Chuyển nguyên khối để
    /// tutora-ai thêm filter mới không phải sửa .NET, và không thể rơi lần nữa.
    /// Schema thật: WebChatRequest.current_filters (tutora-ai/app/models/schemas.py).
    /// </summary>
    public JsonElement? CurrentFilters { get; set; }

    /// <summary>
    /// Chỉ dùng khi AUTHED: phiên chat để lưu lịch sử.
    /// </summary>
    public Guid? SessionId { get; set; }

    /// <summary>
    /// Entity memory: gia sư ĐÃ hiển thị các lượt trước (FE giữ & gửi lại, như CurrentFilters).
    /// </summary>
    public List<AssistantShownTutor> ShownTutors { get; set; } = new();
}

public class AssistantHistoryItem
{
    public string Role { get; set; } = "user";
    public string Content { get; set; } = string.Empty;
}

public class AssistantShownTutor
{
    public string TutorId { get; set; } = string.Empty;
    public string? Name { get; set; }
}

public class AssistantContextDto
{
    public int? SubjectId { get; set; }
    public int? GradeLevelId { get; set; }
    public string? TeachingMode { get; set; }
    public string? City { get; set; }
    public double? MinRate { get; set; }
    public double? MaxRate { get; set; }
    public string? TutorGender { get; set; }
}

