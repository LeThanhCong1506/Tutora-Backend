using MV.DomainLayer.DTO.ResponseModel;

namespace MV.ApplicationLayer.ServiceInterfaces
{
    /// <summary>
    /// Client gọi thẳng Gemini File API + generateContent để phân tích video buổi học.
    /// Không đụng DB — mọi lỗi (upload thất bại, file xử lý lỗi, HTTP lỗi, response không hợp lệ)
    /// đều throw để caller (ClassSessionVideoAiService) tự quyết định nuốt lỗi hay không.
    /// </summary>
    public interface IGeminiVideoAnalysisService
    {
        /// <summary>Upload video lên Gemini File API (resumable upload) — stream thẳng, không buffer cả file vào RAM.</summary>
        Task<GeminiUploadedFile> UploadVideoAsync(
            Stream videoStream, long contentLength, string mimeType, string displayName, CancellationToken ct = default);

        /// <summary>Chờ file chuyển sang state ACTIVE (Gemini xử lý video xong) — poll định kỳ, có timeout.</summary>
        Task WaitForFileActiveAsync(string fileName, CancellationToken ct = default);

        /// <summary>Tóm tắt buổi học bằng tiếng Việt. Tách riêng khỏi chép lời vì đầu ra ngắn hơn 10-15 lần
        /// nên xong sau vài giây — gộp chung 1 lượt gọi thì người dùng phải đợi cả bản chép lời viết xong
        /// mới thấy được tóm tắt (LLM sinh token tuần tự, response chỉ về khi viết hết).</summary>
        Task<string> SummarizeVideoForStudentAsync(string fileUri, string mimeType, CancellationToken ct = default);

        /// <summary>Chép lời (transcript) đầy đủ buổi học. Chạy nền sau khi tóm tắt đã trả cho người dùng,
        /// dùng model riêng (<see cref="MV.DomainLayer.Configuration.GoogleGeminiSettings.TranscriptModel"/>).</summary>
        Task<string> TranscribeVideoAsync(string fileUri, string mimeType, CancellationToken ct = default);

        /// <summary>Sinh nội dung báo cáo có cấu trúc (structured JSON output) cho gia sư.</summary>
        Task<TutorReportAiFillResult> GenerateTutorReportFieldsAsync(string fileUri, string mimeType, CancellationToken ct = default);

        /// <summary>Trả lời câu hỏi tiếp theo dựa trên tóm tắt đã có + lịch sử hội thoại — không cần video nữa.</summary>
        Task<string> AskFollowUpAsync(
            string summaryText, IReadOnlyList<GeminiChatTurn> history, string question, CancellationToken ct = default);
    }
}
