namespace MV.DomainLayer.DTO.RequestModel;

public class LessonQueryRequest
{
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public string? Status { get; set; }
}
