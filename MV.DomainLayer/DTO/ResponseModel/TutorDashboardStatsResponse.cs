namespace MV.DomainLayer.DTO.ResponseModel;

/// <summary>
/// Dashboard statistics for tutor
/// </summary>
public class TutorDashboardStatsResponse
{
    /// <summary>Number of upcoming classSessions (scheduled)</summary>
    public int UpcomingClassSessions { get; set; }

    /// <summary>Number of classSessions completed this month</summary>
    public int CompletedThisMonth { get; set; }

    /// <summary>Total classSessions completed</summary>
    public int TotalCompleted { get; set; }

    /// <summary>
    /// Tiền đã giải ngân về ví trong tháng (VND)
    /// </summary>
    public decimal EarningsThisMonth { get; set; }

    /// <summary>
    /// Tiền của buổi đã dạy trong tháng nhưng chưa quyết toán (VND) — còn nằm
    /// trong escrow, chờ học sinh xác nhận hoặc chờ gia sư gửi báo cáo.
    /// </summary>
    public decimal EarnedPendingThisMonth { get; set; }

    /// <summary>Total earnings (VND)</summary>
    public decimal TotalEarnings { get; set; }

    /// <summary>Current wallet balance (VND)</summary>
    public decimal WalletBalance { get; set; }

    /// <summary>Wallet balance that can be withdrawn or spent (VND)</summary>
    public decimal AvailableBalance { get; set; }

    /// <summary>Frozen balance in escrow (VND)</summary>
    public decimal FrozenBalance { get; set; }

    /// <summary>Total wallet balance including frozen escrow (VND)</summary>
    public decimal TotalBalance { get; set; }

    /// <summary>Pending confirmation classSessions</summary>
    public int PendingConfirmation { get; set; }

    /// <summary>
    /// Tiền của các buổi đã lên lịch nhưng chưa dạy **trong tháng này** (VND) —
    /// sẽ về ví sau khi dạy xong và quyết toán.
    /// </summary>
    public decimal UpcomingEarnings { get; set; }

    /// <summary>Số buổi đã lên lịch trong tháng này, cùng kỳ với UpcomingEarnings</summary>
    public int UpcomingClassSessionsThisMonth { get; set; }

    /// <summary>Số buổi đã dạy xong nhưng tutor chưa gửi báo cáo</summary>
    public int AwaitingReport { get; set; }

    /// <summary>Buổi đã dạy xong đang chờ tutor gửi báo cáo</summary>
    public List<AwaitingReportClassSessionResponse> AwaitingReportClassSessions { get; set; } = new();

    /// <summary>Active disputes</summary>
    public int ActiveDisputes { get; set; }

    /// <summary>Average rating (1-5)</summary>
    public double AverageRating { get; set; }

    /// <summary>Total reviews count</summary>
    public int TotalReviews { get; set; }

    /// <summary>Next upcoming classSessions (up to 20 — mobile dựng lịch tuần)</summary>
    public List<UpcomingClassSessionResponse> NextClassSessions { get; set; } = new();

    /// <summary>Profile status: draft, pending_approval, active, rejected</summary>
    public string? ProfileStatus { get; set; }

    /// <summary>
    /// Whether tutor has at least 1 verified certificate (admin approved).
    /// Used by FE to show specific banner when draft + certificates verified.
    /// </summary>
    public bool HasVerifiedCertificates { get; set; }

    /// <summary>
    /// List of missing required field keys when profile is draft.
    /// Keys: headline, teachingArea, teachingMode, subjects, bio, education, hourlyRate, avatar, video
    /// </summary>
    public List<string>? MissingFields { get; set; }
}

/// <summary>
/// Upcoming classSession summary
/// </summary>
public class UpcomingClassSessionResponse
{
    public int ClassSessionId { get; set; }
    public int? BookingId { get; set; }
    public DateTime ScheduledStart { get; set; }
    public DateTime ScheduledEnd { get; set; }
    public string? StudentName { get; set; }
    public string? SubjectName { get; set; }
    public string? MeetingLink { get; set; }
}

/// <summary>
/// Buổi đã dạy xong nhưng chưa có báo cáo — tiền chỉ chạy tiếp khi tutor gửi.
/// </summary>
public class AwaitingReportClassSessionResponse
{
    public int ClassSessionId { get; set; }
    public int? BookingId { get; set; }
    public DateTime ScheduledStart { get; set; }
    public DateTime ScheduledEnd { get; set; }
    public string? StudentName { get; set; }
    public string? SubjectName { get; set; }

    /// <summary>Giờ tutor rời phòng — mốc để tính buổi đã kết thúc bao lâu.</summary>
    public DateTime? CheckOutTime { get; set; }

    /// <summary>Tiền buổi này, hiện ngay trên thẻ để tutor thấy lý do phải gửi.</summary>
    public decimal ClassSessionPrice { get; set; }
}
