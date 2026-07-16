namespace MV.DomainLayer.DTO.ResponseModel;

/// <summary>
/// Thứ trong tuần (tra cứu). Khớp với day_of_week_id dùng trong lịch rảnh của gia sư.
/// </summary>
public class DayOfWeekResponse
{
    public int DayOfWeekId { get; set; }
    public string DayName { get; set; } = string.Empty;
    public int DayOrder { get; set; }
}
