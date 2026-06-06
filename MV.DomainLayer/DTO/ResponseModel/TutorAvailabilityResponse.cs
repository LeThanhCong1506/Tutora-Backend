using MV.DomainLayer.Constants;

namespace MV.DomainLayer.DTO.ResponseModel
{
    /// <summary>
    /// Response model for tutor availability slot.
    /// Validity window is derived from Createdat — no extra DB column required.
    /// A slot is active from its creation date (Vietnam time) for 30 days.
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
        /// First date this slot is valid (= DATE(Createdat) in Vietnam time).
        /// </summary>
        public DateOnly ValidFrom { get; set; }

        /// <summary>
        /// Last date this slot is valid (= ValidFrom + 30 days).
        /// After this date the tutor must re-register the slot.
        /// </summary>
        public DateOnly ValidTo { get; set; }

        /// <summary>
        /// True when today (Vietnam time) is within [ValidFrom, ValidTo].
        /// Expired or future-only slots return false.
        /// </summary>
        public bool IsActive { get; set; }

        /// <summary>
        /// Human-readable day name (e.g., "Monday", "Tuesday")
        /// </summary>
        public string DayName => Dayofweek switch
        {
            0 => "Sunday",
            1 => "Monday",
            2 => "Tuesday",
            3 => "Wednesday",
            4 => "Thursday",
            5 => "Friday",
            6 => "Saturday",
            _ => DisplayValues.Unknown
        };
    }
}
