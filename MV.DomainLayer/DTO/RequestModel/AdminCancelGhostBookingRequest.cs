using System.ComponentModel.DataAnnotations;

namespace MV.DomainLayer.DTO.RequestModel;

/// <summary>
/// Request body cho staff hủy booking do phụ huynh "nghỉ ngang" — xác minh hoàn toàn ngoài hệ
/// thống (qua tổng đài), không gắn với luồng dispute nào.
/// </summary>
public class AdminCancelGhostBookingRequest
{
    [Required(ErrorMessage = "Vui lòng nhập lý do hủy booking.")]
    [StringLength(2000, MinimumLength = 10, ErrorMessage = "Lý do phải từ 10 đến 2000 ký tự.")]
    public string Reason { get; set; } = null!;
}
