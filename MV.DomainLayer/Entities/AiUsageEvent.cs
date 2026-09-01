using System;

namespace MV.DomainLayer.Entities;

/// <summary>
/// Một lời gọi Gemini: token đã dùng + tiền trả Google, do tutora-ai đẩy về.
/// Khác <see cref="AiUsageMonthly"/> (đếm LƯỢT của user cho hạn mức credit) —
/// bảng này đo CHI PHÍ vận hành, phục vụ trang admin quan sát.
/// </summary>
public partial class AiUsageEvent
{
    public long Id { get; set; }

    /// <summary>Tính năng gọi: 'solve' | 'classroom_generate' | 'zalo_agent' | 'embed'...</summary>
    public string Feature { get; set; } = null!;

    public string Model { get; set; } = null!;

    public int Prompttokens { get; set; }

    public int Outputtokens { get; set; }

    /// <summary>Token "thinking" — Google tính giá như output.</summary>
    public int Thoughtstokens { get; set; }

    /// <summary>Phần prompt được cache — giá rẻ hơn input thường.</summary>
    public int Cachedtokens { get; set; }

    public int Totaltokens { get; set; }

    /// <summary>Tiền do tutora-ai tính sẵn từ bảng giá; SDK Gemini chỉ trả token.</summary>
    public decimal Costusd { get; set; }

    public int? Latencyms { get; set; }

    /// <summary>false = lời gọi lỗi; vẫn ghi để đếm tỉ lệ hỏng.</summary>
    public bool Success { get; set; }

    public string? Error { get; set; }

    public DateTime Createdat { get; set; }
}
