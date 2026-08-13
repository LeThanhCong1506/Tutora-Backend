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

        /// <summary>Tóm tắt + chép lời (transcript) đầy đủ buổi học bằng tiếng Việt, lấy trong CÙNG 1 lượt gọi
        /// (Gemini đã phải "nghe" hết video để tóm tắt, nên xin luôn bản chép lời không tốn thêm 1 lượt phân tích).</summary>
        Task<GeminiVideoStudentAnalysis> AnalyzeVideoForStudentAsync(string fileUri, string mimeType, CancellationToken ct = default);

        /// <summary>Sinh nội dung báo cáo có cấu trúc (structured JSON output) cho gia sư.</summary>
        Task<TutorReportAiFillResult> GenerateTutorReportFieldsAsync(string fileUri, string mimeType, CancellationToken ct = default);

        /// <summary>Trả lời câu hỏi tiếp theo dựa trên tóm tắt đã có + lịch sử hội thoại — không cần video nữa.</summary>
        Task<string> AskFollowUpAsync(
            string summaryText, IReadOnlyList<GeminiChatTurn> history, string question, CancellationToken ct = default);
    }
}
