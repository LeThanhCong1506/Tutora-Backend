using System.ComponentModel.DataAnnotations;

namespace MV.DomainLayer.DTO.RequestModel;

/// <summary>
/// Request FE gửi tới .NET để hỏi AI giải toán trong một phiên chat.
/// .NET sẽ: lưu user message → dựng history từ DB → gọi tutora-ai /solve →
/// stream về FE → lưu assistant message. FE KHÔNG cần tự gửi history.
/// </summary>
public class AiSolveRequest
{
    /// <summary>Đề bài dạng text (hoặc dùng ImageUrl/ImageBase64).</summary>
    public string? Text { get; set; }

    public string? ImageUrl { get; set; }

    public string? ImageBase64 { get; set; }

    public string? Grade { get; set; }

    public string? Chapter { get; set; }
}
