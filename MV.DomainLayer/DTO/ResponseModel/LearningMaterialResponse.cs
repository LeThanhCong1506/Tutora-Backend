namespace MV.DomainLayer.DTO.ResponseModel;

public class LearningMaterialResponse
{
    public int MaterialId { get; set; }
    public string? StudentId { get; set; }
    public int? BookingId { get; set; }
    public string? UploadedBy { get; set; }
    public string OwnerType { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? FileType { get; set; }
    public string FileUrl { get; set; } = string.Empty;
    public int? FileSize { get; set; }
    public bool? IsPublic { get; set; }
    public DateTime? CreatedAt { get; set; }
}
