namespace MV.DomainLayer.DTO.RequestModel;

/// <summary>
/// Query parameters for the admin booking list.
///
/// Gom thành một object thay vì truyền rời: danh sách bộ lọc đã tới mức mà một
/// chuỗi truyền lệch vị trí (tutorId / parentId / search đều là string?) sẽ lọc
/// sai mà không có lỗi biên dịch nào.
/// </summary>
public class AdminBookingQueryRequest
{
    /// <summary>1-based page number.</summary>
    public int Page { get; set; } = 1;

    /// <summary>Items per page (max 100).</summary>
    public int PageSize { get; set; } = 20;

    /// <summary>Filter by BookingStatus constant. Null = all.</summary>
    public string? Status { get; set; }

    /// <summary>Filter by teaching mode (online/offline/hybrid). Null = all.</summary>
    public string? TeachingMode { get; set; }

    /// <summary>Filter by tutor user id. Null = all.</summary>
    public string? TutorId { get; set; }

    /// <summary>Filter by parent user id. Null = all.</summary>
    public string? ParentId { get; set; }

    /// <summary>Filter by subject id. Null = all.</summary>
    public int? SubjectId { get; set; }

    /// <summary>Filter bookings created on or after this UTC datetime.</summary>
    public DateTime? From { get; set; }

    /// <summary>Filter bookings created on or before this UTC datetime.</summary>
    public DateTime? To { get; set; }

    /// <summary>
    /// Case-insensitive keyword matched against tutor name, parent name, parent
    /// email or payment code. Không khớp mã đặt lịch — dùng <see cref="BookingId"/> cho việc đó.
    /// </summary>
    public string? Search { get; set; }

    /// <summary>Filter by the booking's own id (mã đặt lịch). Null = all.</summary>
    public int? BookingId { get; set; }

    /// <summary>
    /// Filter to the booking that owns this class session (id buổi học).
    /// Trả về tối đa một booking, vì mỗi buổi học chỉ thuộc một khóa.
    /// </summary>
    public int? ClassSessionId { get; set; }

    /// <summary>
    /// Order by creation time: "asc" = oldest first, "desc" (default) = newest first.
    /// See <see cref="MV.DomainLayer.Constants.ListSortDirection"/>.
    /// </summary>
    public string? SortDirection { get; set; }
}
