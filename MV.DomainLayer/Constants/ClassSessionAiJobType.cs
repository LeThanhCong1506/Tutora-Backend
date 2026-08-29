namespace MV.DomainLayer.Constants;

public static class ClassSessionAiJobType
{
    public const string StudentSummary = "student_summary";
    public const string TutorReportFill = "tutor_report_fill";
    /// <summary>Tổng hợp text-only từ các tóm tắt student_summary của mọi buổi trong 1 chuỗi
    /// bù/phụ/học lại — không upload video riêng, xem RunChainSummaryJobAsync.</summary>
    public const string ChainSummary = "chain_summary";
    /// <summary>Job chạy ngầm ngay khi video buổi học relay xong lên Drive — chỉ tải/tách audio/upload
    /// lên Gemini trước, không gọi model sinh nội dung gì cả. Mục đích duy nhất là làm "nóng" cache
    /// GeminiFileUri (xem EnsureUploadedFileAsync) để tới lúc học sinh/gia sư bấm tóm tắt/điền báo cáo,
    /// bước tải+transcode+upload tốn thời gian nhất đã xong từ trước.</summary>
    public const string Prewarm = "prewarm";
}
