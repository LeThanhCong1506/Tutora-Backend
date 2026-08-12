namespace MV.DomainLayer.DTO.ResponseModel;

/// <summary>File đã upload lên Gemini File API — Name dùng để poll trạng thái, Uri dùng để tham chiếu trong generateContent.</summary>
public sealed record GeminiUploadedFile(string Name, string Uri);

/// <summary>Một lượt hội thoại dùng để dựng lại context cho follow-up chat. Role: "user" | "assistant".</summary>
public sealed record GeminiChatTurn(string Role, string Content);

/// <summary>Kết quả phân tích video cho học sinh — tóm tắt + bản chép lời đầy đủ, lấy trong CÙNG 1 lượt gọi Gemini.</summary>
public sealed record GeminiVideoStudentAnalysis(string Summary, string Transcript);

/// <summary>Kết quả AI tự động điền báo cáo buổi học cho gia sư — khớp 3 field free-text của <c>ClassSessionReport</c>.</summary>
public sealed class TutorReportAiFillResult
{
    public string LessonContent { get; set; } = string.Empty;
    public string Homework { get; set; } = string.Empty;
    public string TutorNotes { get; set; } = string.Empty;
}
