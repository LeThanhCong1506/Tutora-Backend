namespace MV.DomainLayer.DTO.ResponseModel.Question;

public class ChapterResponse
{
    public int Id { get; set; }
    public int SubjectId { get; set; }
    public int GradeLevelId { get; set; }
    public string Slug { get; set; } = null!;
    public string Name { get; set; } = null!;
    public int DisplayOrder { get; set; }
}

public class QuestionTypeResponse
{
    public int Id { get; set; }
    public string Slug { get; set; } = null!;
    public string Name { get; set; } = null!;
    public int DisplayOrder { get; set; }
}

/// <summary>
/// Chương kèm thông tin quản trị cho CMS.
/// </summary>
public class AdminChapterResponse : ChapterResponse
{
    public string? SubjectName { get; set; }
    public string? GradeName { get; set; }
    public bool IsActive { get; set; }
    public int QuestionCount { get; set; }
}

/// <summary>
/// Loại câu hỏi kèm thông tin quản trị cho CMS
/// </summary>
public class AdminQuestionTypeResponse : QuestionTypeResponse
{
    public bool IsActive { get; set; }
    public int QuestionCount { get; set; }
}
