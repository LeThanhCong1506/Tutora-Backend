using System;
using System.Collections.Generic;

namespace MV.DomainLayer.DTO.RequestModel;

/// <summary>
/// Lô sự kiện dùng Gemini do tutora-ai gửi về. Gửi theo lô để 1 phiên giải bài
/// (nhiều lời gọi) chỉ tốn 1 request mạng.
/// </summary>
public class AiUsageIngestRequest
{
    public List<AiUsageEventRequest> Events { get; set; } = [];
}

/// <summary>Một lời gọi Gemini. Token lấy từ usage_metadata, tiền do tutora-ai tính sẵn.</summary>
public class AiUsageEventRequest
{
    public string Feature { get; set; } = null!;
    public string Model { get; set; } = null!;

    public int PromptTokens { get; set; }
    public int OutputTokens { get; set; }
    public int ThoughtsTokens { get; set; }
    public int CachedTokens { get; set; }
    public int TotalTokens { get; set; }

    public decimal CostUsd { get; set; }

    public int? LatencyMs { get; set; }
    public bool Success { get; set; } = true;
    public string? Error { get; set; }

    /// <summary>Thời điểm gọi (UTC). Bỏ trống = lúc server nhận.</summary>
    public DateTime? CreatedAt { get; set; }
}
