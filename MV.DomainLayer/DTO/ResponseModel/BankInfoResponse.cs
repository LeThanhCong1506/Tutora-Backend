namespace MV.DomainLayer.DTO.ResponseModel;

public record BankInfoResponse
{
    public string Code { get; init; } = string.Empty;
    public string ShortName { get; init; } = string.Empty;
    public string FullName { get; init; } = string.Empty;
    public string? LogoUrl { get; init; }
    public string? Bin { get; init; }
    public bool SupportsInstantTransfer { get; init; }
}
