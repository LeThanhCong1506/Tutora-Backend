using System.ComponentModel.DataAnnotations;

namespace MV.DomainLayer.DTO.RequestModel;

public class CreateBookingRequest
{
    // Bắt buộc khi Parent đặt hộ con.
    // Khi Student tự đặt, có thể bỏ qua — BE sẽ tự resolve từ JWT.
    public string? StudentId { get; set; }

    [Required]
    public string TutorId { get; set; } = null!;

    public int? SubjectId { get; set; }

    [Required]
    public int TutorSubjectGradePriceId { get; set; }

    [Required]
    public int PackageId { get; set; }

    public int? TotalSessions { get; set; }

    public List<ScheduleItemRequest>? Schedule { get; set; }

    public List<FlexibleBookingSlotRequest>? FlexibleSlots { get; set; }

    [Required]
    public DateTime StartDate { get; set; }

    public string? LocationCity { get; set; }
    public string? LocationDistrict { get; set; }
    public string? LocationWard { get; set; }
    public string? LocationDetail { get; set; }
    public string? PromotionCode { get; set; }
}

public class FlexibleBookingSlotRequest
{
    [Required]
    public DateTime ScheduledStart { get; set; }

    [Required]
    public DateTime ScheduledEnd { get; set; }
}
