using System.ComponentModel.DataAnnotations;
using MV.DomainLayer.Constants;

namespace MV.DomainLayer.DTO.RequestModel;

public class AiChatSessionCreateRequest
{
    /// <summary>Loại chat: "homework" | "tutor_matching". Mặc định "homework".</summary>
    public string SessionType { get; set; } = ChatSessionType.Homework;

    [StringLength(255, ErrorMessage = "Tiêu đề không được quá 255 ký tự")]
    public string? Title { get; set; }
}
