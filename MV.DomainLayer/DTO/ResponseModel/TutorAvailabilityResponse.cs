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
        /// Human-readable day name (e.g., "Monday", "Tuesday")
        /// </summary>
        public string DayName => Dayofweek switch
        {
            1 => "Monday",
            2 => "Tuesday",
            3 => "Wednesday",
            4 => "Thursday",
            5 => "Friday",
            6 => "Saturday",
            7 => "Sunday",
            _ => DisplayValues.Unknown
        };
    }
}
