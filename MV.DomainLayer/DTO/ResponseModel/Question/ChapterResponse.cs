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
