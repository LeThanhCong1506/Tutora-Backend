namespace MV.DomainLayer.DTO.ResponseModel;

/// <summary>
/// Tỷ lệ phí nền tảng đang áp dụng, dạng phân số (0.1 = 10%) đúng như lưu trong
/// <c>system_configs</c> — không nhân sẵn 100 để phía gọi không phải đoán đơn vị.
/// </summary>
public class BookingFeeRatesResponse
{
    /// <summary>Phần phụ huynh trả THÊM trên học phí gốc.</summary>
    public decimal ParentFeePercent { get; set; }

    /// <summary>Phần TRỪ vào khoản gia sư nhận.</summary>
    public decimal TutorFeePercent { get; set; }
}
