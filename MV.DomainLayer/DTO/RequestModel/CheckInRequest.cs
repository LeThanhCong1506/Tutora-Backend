using System.ComponentModel.DataAnnotations;

namespace MV.DomainLayer.DTO.RequestModel;

/// <summary>
/// Request for tutor check-in at classSession start
/// </summary>
public class CheckInRequest
{
    /// <summary>
    /// Optional note for check-in
    /// </summary>
    public string? Note { get; set; }
}
