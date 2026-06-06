using System.ComponentModel.DataAnnotations;

namespace MV.DomainLayer.DTO.RequestModel
{
    public class SendMessageRequest
    {
        [Required]
        [StringLength(2000)]
        public string Content { get; set; }

        [Required]
        public string RoomId { get; set; }
    }
}
