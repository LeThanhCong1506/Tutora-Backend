namespace MV.DomainLayer.Constants;

public static class ClassSessionAiJobType
{
    public const string StudentSummary = "student_summary";
    public const string TutorReportFill = "tutor_report_fill";
    /// <summary>Tổng hợp text-only từ các tóm tắt student_summary của mọi buổi trong 1 chuỗi
    /// bù/phụ/học lại — không upload video riêng, xem RunChainSummaryJobAsync.</summary>
    public const string ChainSummary = "chain_summary";
}
