using MV.DomainLayer.Constants;

namespace MV.DomainLayer.DTO.ResponseModel
{
    /// <summary>
    /// Response model for tutor availability slot.
    /// </summary>
    public class TutorAvailabilityResponse
    {
        public int Availabilityid { get; set; }
        public string Tutorid { get; set; } = string.Empty;
        public int Dayofweek { get; set; }
        public string Starttime { get; set; } = string.Empty;
        public string Endtime { get; set; } = string.Empty;
        public DateTime Createdat { get; set; }

        /// <summary>
        /// Tên thứ tiếng Việt (khớp với dữ liệu bảng days_of_week).
        /// </summary>
        public string DayName => Dayofweek switch
        {
            1 => "Thứ Hai",
            2 => "Thứ Ba",
            3 => "Thứ Tư",
            4 => "Thứ Năm",
            5 => "Thứ Sáu",
            6 => "Thứ Bảy",
            7 => "Chủ Nhật",
            _ => DisplayValues.Unknown
        };
    }
}
