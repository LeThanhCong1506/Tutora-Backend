namespace MV.DomainLayer.DTO;

/// <summary>
/// Một tệp đính kèm của báo cáo buổi học. Trước đây cột `ClassSessionReport.Attachments` chỉ lưu
/// mảng URL trần nên phía phụ huynh phải hiển thị tên file kỹ thuật do storage sinh ra
/// (`{Guid}_{tên gốc}`); <see cref="Description"/> cho gia sư đặt nhãn dễ hiểu cho từng tệp.
/// </summary>
public class ReportAttachment
{
    public string Url { get; set; } = null!;

    /// <summary>Nhãn gia sư đặt cho tệp (vd "Đề bài buổi 5"). Rỗng thì FE tự lấy tên file.</summary>
    public string? Description { get; set; }
}
