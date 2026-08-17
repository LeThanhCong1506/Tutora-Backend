namespace MV.DomainLayer.Constants;

/// <summary>Giai đoạn con của job student_summary — chỉ để hiện thông báo phù hợp cho người dùng, không ảnh hưởng logic.</summary>
public static class ClassSessionAiJobStage
{
    /// <summary>Đang nghe video để viết tóm tắt (Status=Processing).</summary>
    public const string Analyzing = "analyzing";

    /// <summary>Tóm tắt đã xong và trả cho người dùng rồi (Status=Completed), bản chép lời còn đang
    /// chạy nền. Nhờ mốc này FE phân biệt được "hội thoại đang tạo" với "buổi học không có hội thoại"
    /// (job cũ tạo trước khi có tính năng transcript).</summary>
    public const string Transcribing = "transcribing";
}
