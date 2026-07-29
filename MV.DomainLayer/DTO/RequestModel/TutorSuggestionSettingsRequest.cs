using System.ComponentModel.DataAnnotations;

namespace MV.DomainLayer.DTO.RequestModel;

public class TutorSuggestionSettingsRequest
{
    public bool Enabled { get; set; } = true;

    /// <summary>Số bài cùng chương trong một phiên thì mới gợi ý.</summary>
    [Range(1, 20, ErrorMessage = "Ngưỡng số bài phải từ 1 đến 20.")]
    public int MinChapterCount { get; set; }

    /// <summary>Bỏ qua tín hiệu mà classifier không chắc chắn.</summary>
    [Range(0, 1, ErrorMessage = "Ngưỡng tin cậy phải từ 0 đến 1.")]
    public float MinConfidence { get; set; }

    /// <summary>Số gia sư trả về cho slider.</summary>
    [Range(1, 20, ErrorMessage = "Số gia sư phải từ 1 đến 20.")]
    public int TopK { get; set; }
}

/// <summary>Học sinh bật/tắt nhận gợi ý gia sư trong app giải bài.</summary>
public class StudentPreferencesRequest
{
    public bool TutorSuggestionEnabled { get; set; }
}
