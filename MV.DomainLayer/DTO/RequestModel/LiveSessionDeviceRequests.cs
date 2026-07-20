using System.ComponentModel.DataAnnotations;

namespace MV.DomainLayer.DTO.RequestModel;

public class LiveSessionJoinRequest
{
    [Required]
    public string ParticipationId { get; set; } = string.Empty;

    [Required]
    public string DeviceId { get; set; } = string.Empty;

    [Required]
    [MaxLength(120)]
    public string DeviceLabel { get; set; } = string.Empty;
}

public sealed class LiveSessionTakeoverRequest : LiveSessionJoinRequest
{
    [Required]
    public string ExpectedActiveLeaseId { get; set; } = string.Empty;
}

public sealed class LiveSessionLeaseRequest
{
    [Required]
    public string ParticipationId { get; set; } = string.Empty;

    [Required]
    public string LeaseId { get; set; } = string.Empty;
}
