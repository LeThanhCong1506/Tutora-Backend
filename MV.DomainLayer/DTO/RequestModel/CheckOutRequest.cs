using System.ComponentModel.DataAnnotations;

namespace MV.DomainLayer.DTO.RequestModel;

/// <summary>
/// Request for tutor check-out at lesson end
/// </summary>
public class CheckOutRequest
{
    /// <summary>
    /// Optional note for check-out
    /// </summary>
    public string? Note { get; set; }
}
