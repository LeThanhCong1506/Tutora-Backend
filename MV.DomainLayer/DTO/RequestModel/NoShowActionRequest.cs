using System.ComponentModel.DataAnnotations;

namespace MV.DomainLayer.DTO.RequestModel;

/// <summary>
/// Request for parent to choose action for tutor no-show
/// </summary>
public class NoShowActionRequest
{
    /// <summary>
    /// Action type: free_session, makeup, change_tutor
    /// </summary>
    [Required(ErrorMessage = "Loại hành động là bắt buộc")]
    public string ActionType { get; set; } = null!;

    /// <summary>
    /// New scheduled start time (required for makeup)
    /// </summary>
    public DateTime? NewScheduledStart { get; set; }

    /// <summary>
    /// Additional note
    /// </summary>
    [StringLength(500)]
    public string? Note { get; set; }
}
