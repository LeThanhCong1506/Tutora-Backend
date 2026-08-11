namespace MV.DomainLayer.Constants;

/// <summary>Giai đoạn con trong lúc job student_summary đang Processing — chỉ để hiện thông báo phù hợp cho người dùng, không ảnh hưởng logic.</summary>
public static class ClassSessionAiJobStage
{
    public const string Analyzing = "analyzing";
    public const string Verifying = "verifying";
}
