using System.ComponentModel.DataAnnotations;
using MV.DomainLayer.Constants;

namespace MV.DomainLayer.DTO.RequestModel;

public class AiChatMessageCreateRequest
{
    /// <summary>Vai trò tin nhắn: "user" | "assistant" | "system". Mặc định "user".</summary>
    public string Role { get; set; } = ChatHistoryRole.User;

    [Required(ErrorMessage = "Nội dung tin nhắn không được để trống")]
    public string Content { get; set; } = null!;

    public string? ImageUrl { get; set; }

    /// <summary>Cột thống kê nội bộ — lưu DB nhưng không trả ra response.</summary>
    public string? Grade { get; set; }

    /// <summary>Câu trả lời có dùng RAG hay không — thống kê nội bộ.</summary>
    public bool RagUsed { get; set; }

    public object? Metadata { get; set; }
}
