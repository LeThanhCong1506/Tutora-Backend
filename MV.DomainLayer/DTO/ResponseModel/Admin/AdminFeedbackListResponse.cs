namespace MV.DomainLayer.DTO.ResponseModel.Admin;

/// <summary>
/// Paged wrapper for GET /api/feedbacks/admin.
/// Không dùng <c>PagedList&lt;T&gt;</c> vì kiểu đó kế thừa <c>List&lt;T&gt;</c> nên serialize ra
/// mảng JSON thuần và mất sạch metadata phân trang — CMS cần TotalCount để phân trang chuẩn.
/// </summary>
public class AdminFeedbackListResponse
{
    public List<FeedbackListResponse> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }

    /// <summary>Thống kê trên toàn bộ tập đã lọc, không phải chỉ trang hiện tại.</summary>
    public AdminFeedbackStats Stats { get; set; } = new();
}

/// <summary>
/// Số liệu tổng quan cho hàng KPI ở đầu trang kiểm duyệt.
/// </summary>
public class AdminFeedbackStats
{
    public int TotalCount { get; set; }
    public int VisibleCount { get; set; }
    public int HiddenCount { get; set; }
    public double AverageRating { get; set; }
}
