using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using MV.DomainLayer.Constants;

namespace MV.DomainLayer.DTO.RequestModel;

public class ChatMessageCreateRequest
{
    [Required(ErrorMessage = "Nội dung tin nhắn không được để trống")]
    [StringLength(2000, ErrorMessage = "Nội dung tin nhắn không được quá 2000 ký tự")]
    public string Content { get; set; } = null!;

    public string MessageType { get; set; } = ChatMessageType.Text;

    public object? Metadata { get; set; }
}
