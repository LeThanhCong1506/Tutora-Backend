using System.ComponentModel.DataAnnotations;

namespace MV.DomainLayer.DTO.RequestModel;

public class TopupRequest
{
    [Required]
    [Range(10000, 50000000, ErrorMessage = "Số tiền nạp phải từ 10,000 đến 50,000,000 VND")]
    public decimal Amount { get; set; }
}
