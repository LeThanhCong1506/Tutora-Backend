namespace MV.DomainLayer.DTO.ResponseModel;

/// <summary>
/// Số liệu tổng quan trên Home của phụ huynh.
/// </summary>
public class ParentHomeStatsResponse
{
    /// <summary>Buổi học trong tuần này (bỏ buổi đã huỷ và `reserved`).</summary>
    public int SessionsThisWeek { get; set; }

    /// <summary>Số con CÓ booking đang hoạt động — khác tổng số con.</summary>
    public int ChildrenLearning { get; set; }

    /// <summary>Tổng số con của phụ huynh.</summary>
    public int ChildrenTotal { get; set; }

    /// <summary>Buổi chờ phụ huynh xác nhận sau khi gia sư gửi báo cáo.</summary>
    public int PendingConfirmation { get; set; }
}
