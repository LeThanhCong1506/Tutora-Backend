using Microsoft.AspNetCore.Http;

namespace MV.DomainLayer.DTO.RequestModel;

public class UploadMaterialRequest
{
    public IFormFile File { get; set; } = null!;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsPublic { get; set; }
}
