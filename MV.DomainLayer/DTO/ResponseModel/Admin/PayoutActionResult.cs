namespace MV.DomainLayer.DTO.ResponseModel.Admin;

/// <summary>
/// Kết quả khi admin phê duyệt yêu cầu rút tiền
/// </summary>
public class ApproveResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? EstimatedTime { get; set; }
}

/// <summary>
/// Kết quả khi admin từ chối yêu cầu rút tiền
/// </summary>
public class RejectResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
}
