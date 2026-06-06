namespace MV.DomainLayer.DTO.RequestModel
{
    public class StudyRecommendationRequest
    {
        public string UserId { get; set; } = string.Empty;
        public int? AttemptId { get; set; }
    }
}
