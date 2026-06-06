namespace MV.DomainLayer.DTO.ResponseModel
{
    public class NotificationResponse
    {
        public int Notificationid { get; set; }
        public string Userid { get; set; } = null!;
        public string Title { get; set; } = null!;
        public string Message { get; set; } = null!;
        /// <summary>Notification type — e.g. booking_new, payment_success, warning. See NotificationType constants.</summary>
        public string? Type { get; set; }
        /// <summary>ID of the referenced entity (bookingId, lessonId, warningId…) for deep-link navigation.</summary>
        public string? Referenceid { get; set; }
        public bool? Isread { get; set; }
        public DateTime? Createdat { get; set; }

        // Optional: Include user information
        public string? Username { get; set; }
        public string? UserFullname { get; set; }
    }
}
