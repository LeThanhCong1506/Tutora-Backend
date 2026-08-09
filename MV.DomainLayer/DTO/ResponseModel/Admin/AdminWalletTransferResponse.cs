namespace MV.DomainLayer.DTO.ResponseModel.Admin;

/// <summary>
/// Kết quả một lần admin/staff chuyển tiền chủ động vào ví user.
/// </summary>
public class AdminWalletTransferResponse
{
    public int TransferId { get; set; }
    public string RecipientUserId { get; set; } = string.Empty;
    public string? RecipientName { get; set; }
    public string? RecipientRole { get; set; }
    public decimal Amount { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string CreatedBy { get; set; } = string.Empty;
    public string? CreatedByName { get; set; }
    public DateTime CreatedAt { get; set; }

    /// <summary>Số dư ví của người nhận NGAY SAU khi cộng tiền — chỉ có giá trị lúc vừa tạo.</summary>
    public decimal? RecipientNewBalance { get; set; }
}

/// <summary>
/// Paged wrapper for GET /api/admin/payouts/transfers — theo đúng khuôn
/// <see cref="AdminFeedbackListResponse"/> vì PagedList&lt;T&gt; kế thừa List&lt;T&gt; nên
/// serialize ra mảng JSON thuần, mất hết metadata phân trang.
/// </summary>
public class AdminWalletTransferListResponse
{
    public List<AdminWalletTransferResponse> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}
