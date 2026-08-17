namespace MV.DomainLayer.DTO.ResponseModel.Admin;

/// <summary>Kết quả một lần admin nạp tiền thật vào quỹ hệ thống.</summary>
public class SystemFundTopupResponse
{
    public long TopupId { get; set; }
    public decimal Amount { get; set; }
    public string Reason { get; set; } = string.Empty;

    /// <summary>Signed URL có hạn dùng, tương tự ảnh biên lai payout.</summary>
    public string? ProofImageUrl { get; set; }

    public string CreatedBy { get; set; } = string.Empty;
    public string? CreatedByName { get; set; }
    public DateTime CreatedAt { get; set; }

    /// <summary>Số dư quỹ NGAY SAU lần nạp này — chỉ có giá trị lúc vừa tạo.</summary>
    public decimal? FundBalanceAfter { get; set; }
}

/// <summary>
/// Paged wrapper for GET /api/admin/payouts/fund/topups — theo đúng khuôn
/// <see cref="AdminWalletTransferListResponse"/>.
/// </summary>
public class SystemFundTopupListResponse
{
    public List<SystemFundTopupResponse> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}
