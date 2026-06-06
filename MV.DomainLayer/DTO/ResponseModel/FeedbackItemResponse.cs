namespace MV.DomainLayer.DTO.ResponseModel
{
    /// <summary>
    /// Response model for a single feedback item with reviewer info
    /// </summary>
    public class FeedbackItemResponse
    {
        public int FeedbackId { get; set; }
        public string? FromUserId { get; set; }
        public string? FromUserName { get; set; }
        public string? FromUserAvatar { get; set; }
        public int? Rating { get; set; }
        public string? Comment { get; set; }
        public string? ReplyComment { get; set; }
        public DateTime? RepliedAt { get; set; }
        public DateTime? CreatedAt { get; set; }
        public string? InitialGoal { get; set; }
        public string? ActualResult { get; set; }
        public string? CourseDuration { get; set; }
    }
}
