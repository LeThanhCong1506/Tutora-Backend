using System;
using System.Collections.Generic;

namespace MV.DomainLayer.DTO.ResponseModel.Admin;

/// <summary>
/// Thống kê CHI PHÍ gọi Gemini (tiền trả Google) — khác
/// <see cref="AdminAiRevenueResponse"/> là doanh thu bán credit cho user.
/// </summary>
public class AdminAiUsageResponse
{
    public AdminAiUsageTotals Totals { get; set; } = new();

    /// <summary>Chuỗi thời gian theo ngày để vẽ biểu đồ.</summary>
    public List<AdminAiUsagePoint> Timeline { get; set; } = [];

    public List<AdminAiUsageBreakdown> ByModel { get; set; } = [];

    public List<AdminAiUsageBreakdown> ByFeature { get; set; } = [];
}

/// <summary>Tỉ giá USD→VND dùng để quy đổi chi phí hiển thị.</summary>
public class AiUsageRateResponse
{
    public decimal Rate { get; set; }

    /// <summary>true = admin tự nhập, false = lấy từ API tỉ giá thị trường.</summary>
    public bool IsManual { get; set; }

    /// <summary>Thời điểm tỉ giá này được ghi nhận. Null khi chưa từng đặt tay.</summary>
    public DateTime? UpdatedAt { get; set; }

    /// <summary>Nguồn tỉ giá, để UI nói rõ đang lấy từ đâu.</summary>
    public string Source { get; set; } = null!;
}

/// <summary>Tổng của kỳ đang xem, kèm kỳ liền trước để tính % thay đổi.</summary>
public class AdminAiUsageTotals
{
    public long Calls { get; set; }
    public long TotalTokens { get; set; }
    public long PromptTokens { get; set; }
    public long OutputTokens { get; set; }

    /// <summary>Token "thinking" — Google tính giá như output, tách ra để thấy phần ẩn này.</summary>
    public long ThoughtsTokens { get; set; }

    public long CachedTokens { get; set; }
    public decimal CostUsd { get; set; }

    /// <summary>Số lời gọi lỗi (vẫn tính vào Calls).</summary>
    public long FailedCalls { get; set; }

    public int? AvgLatencyMs { get; set; }

    public long PrevCalls { get; set; }
    public decimal PrevCostUsd { get; set; }
    public long PrevTotalTokens { get; set; }
}

/// <summary>Một mốc ngày trên biểu đồ.</summary>
public class AdminAiUsagePoint
{
    public DateOnly Date { get; set; }
    public long Calls { get; set; }
    public long TotalTokens { get; set; }
    public decimal CostUsd { get; set; }
}

/// <summary>Một dòng gom nhóm theo model hoặc theo feature.</summary>
public class AdminAiUsageBreakdown
{
    /// <summary>Tên model ('gemini-2.5-flash') hoặc tên feature ('solve').</summary>
    public string Key { get; set; } = null!;

    public long Calls { get; set; }
    public long TotalTokens { get; set; }
    public decimal CostUsd { get; set; }
    public long FailedCalls { get; set; }

    /// <summary>Tỉ lệ % chi phí trên tổng kỳ — để UI khỏi tự tính.</summary>
    public decimal CostShare { get; set; }
}
