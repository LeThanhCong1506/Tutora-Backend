namespace MV.DomainLayer.DTO.ResponseModel;

public class CccdUploadResponse
{
    public bool OcrSuccess { get; set; }
    public string? IdentityNumber { get; set; }
    public string? FullName { get; set; }
    public string? DateOfBirth { get; set; }
    public string? Gender { get; set; }
    public string? Address { get; set; }
    public string Message { get; set; } = string.Empty;
}
