namespace MV.DomainLayer.DTO.RequestModel
{
    public class DashboardTrendRequest
    {
        /// <summary>Ngày bắt đầu, format: yyyy-MM-dd. Mặc định: 30 ngày trước.</summary>
        public string? From { get; set; }

        /// <summary>Ngày kết thúc, format: yyyy-MM-dd. Mặc định: hôm nay.</summary>
        public string? To { get; set; }

        /// <summary>
        /// Độ chi tiết nhóm: auto | day | week | month.
        /// auto: ≤31 ngày → day, ≤90 ngày → week, >90 ngày → month.
        /// </summary>
        public string Bucket { get; set; } = "auto";

        /// <summary>Timezone để nhóm ngày. Mặc định: Asia/Ho_Chi_Minh (UTC+7).</summary>
        public string Timezone { get; set; } = "Asia/Ho_Chi_Minh";
    }
}
