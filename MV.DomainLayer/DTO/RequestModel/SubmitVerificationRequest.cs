namespace MV.DomainLayer.DTO.RequestModel;

/// <summary>
/// Request body for submitting tutor identity verification image paths.
/// </summary>
public class SubmitVerificationRequest
{
    public string FrontImgPath { get; set; } = string.Empty;
    public string BackImgPath { get; set; } = string.Empty;
}
