namespace MV.DomainLayer.DTO.ResponseModel
{
    public class StudyRecommendationResponse
    {
        public string UserId { get; set; } = string.Empty;
        public int TestId { get; set; }
        public float Score { get; set; }
        //public List<WeakTopicDetail> WeakTopics { get; set; } = new();
        public string AiRecommendation { get; set; } = string.Empty;
        public DateTime GeneratedAt { get; set; } = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;
    }
}
