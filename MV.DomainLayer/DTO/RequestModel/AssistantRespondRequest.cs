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

    /// <summary>Filter đã tích luỹ qua các lượt (giá/giới tính/môn...) — FE giữ, forward sang AI.</summary>
    public AssistantFiltersDto? CurrentFilters { get; set; }

    /// <summary>
    /// Chỉ dùng khi AUTHED: phiên chat để lưu lịch sử.
    /// </summary>
    public Guid? SessionId { get; set; }
}

public class AssistantHistoryItem
{
    public string Role { get; set; } = "user";
    public string Content { get; set; } = string.Empty;
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

public class AssistantFiltersDto
{
    public double? MinRate { get; set; }
    public double? MaxRate { get; set; }
    public string? TutorGender { get; set; }
    public int? SubjectId { get; set; }
    public int? DesiredCount { get; set; }
}
