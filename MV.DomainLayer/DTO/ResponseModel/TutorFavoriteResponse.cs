namespace MV.DomainLayer.DTO.ResponseModel;

/// <summary>
/// One saved tutor, carrying enough to render a card without a second round trip.
/// </summary>
public class TutorFavoriteResponse
{
    public string TutorId { get; set; } = null!;
    public string? FullName { get; set; }
    public string? AvatarUrl { get; set; }
    public string? Headline { get; set; }
    public string? Education { get; set; }
    public string? Degree { get; set; }

    public double? AverageRating { get; set; }
    public int? TotalReviews { get; set; }

    /// <summary>Số buổi đã dạy — cùng định nghĩa với thẻ gia sư ở trang tìm kiếm.</summary>
    public int TotalClassSessions { get; set; }

    public decimal? MinPricePerHour { get; set; }
    public List<string> Subjects { get; set; } = new();

    /// <summary>
    /// False when the tutor is no longer bookable — suspended, unpublished, or has stopped
    /// accepting bookings. The row stays in the list so the saver can see what happened
    /// rather than having it silently disappear.
    /// </summary>
    public bool IsAvailable { get; set; }

    public DateTime SavedAt { get; set; }
}
