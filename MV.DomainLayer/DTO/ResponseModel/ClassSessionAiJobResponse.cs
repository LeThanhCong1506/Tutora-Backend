namespace MV.DomainLayer.DTO.ResponseModel;

/// <summary>Trạng thái/kết quả 1 job Gemini phân tích video — dùng cho cả tóm tắt học sinh lẫn auto-fill báo cáo gia sư.</summary>
public class ClassSessionAiJobResponse
{
    public Guid JobId { get; set; }
    /// <summary>"none" khi chưa từng trigger (không có job nào) — 3 giá trị còn lại khớp <see cref="Constants.ClassSessionAiJobStatus"/>.</summary>
    public string Status { get; set; } = "none";
    /// <summary>Giai đoạn con khi Status="processing" (student_summary) — <see cref="Constants.ClassSessionAiJobStage"/>. Null khi không áp dụng.</summary>
    public string? Stage { get; set; }
    public string? ResultText { get; set; }
    /// <summary>Bản chép lời (transcript) đầy đủ — chỉ có ở job student_summary, lấy cùng lượt gọi Gemini với tóm tắt.</summary>
    public string? TranscriptText { get; set; }
    public TutorReportAiFillResult? ResultJson { get; set; }
    public string? ErrorMessage { get; set; }
}

public class ClassSessionVideoChatMessageResponse
{
    public string Role { get; set; } = null!;
    public string Content { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
}
