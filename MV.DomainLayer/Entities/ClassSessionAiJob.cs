namespace MV.DomainLayer.Entities;

/// <summary>
/// Một lượt Gemini phân tích video buổi học — dùng chung cho tóm tắt gửi học sinh
/// (student_summary) và tự động điền báo cáo gửi gia sư (tutor_report_fill). GeminiFileUri
/// được cache lại để 2 loại job của cùng buổi học không phải upload lại video nhiều lần.
/// </summary>
public class ClassSessionAiJob
{
    public Guid JobId { get; set; }
    public int Classsessionid { get; set; }
    public string Jobtype { get; set; } = null!;
    public string Requestedbyuserid { get; set; } = null!;
    public string Status { get; set; } = null!;
    /// <summary>Giai đoạn con khi Status=Processing (student_summary) — <see cref="Constants.ClassSessionAiJobStage"/>. Null khi không áp dụng.</summary>
    public string? Stage { get; set; }
    public string? Resulttext { get; set; }
    public string? Transcripttext { get; set; }
    public string? Resultjson { get; set; }
    public string? Geminifileuri { get; set; }
    public string? Geminifilename { get; set; }
    public DateTime? Geminifileexpiresat { get; set; }
    public string? Errormessage { get; set; }
    public DateTime Createdat { get; set; }
    public DateTime? Completedat { get; set; }
    public virtual ClassSession ClassSession { get; set; } = null!;
}
