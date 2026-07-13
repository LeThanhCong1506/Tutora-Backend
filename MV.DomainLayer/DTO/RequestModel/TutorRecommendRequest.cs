namespace MV.DomainLayer.DTO.RequestModel
{
    public class TutorRecommendRequest
    {
        public int? SubjectId { get; set; }
        public int? GradeLevelId { get; set; }          
        public string? GradeLevel { get; set; }         
        public string? TeachingMode { get; set; }       
        public string? City { get; set; }
        public string? District { get; set; }
        /// <summary>Ưu tiên giới tính gia sư. "male" | "female" | null (không yêu cầu).</summary>
        public string? Gender { get; set; }
        public decimal? MinRate { get; set; }
        public decimal? MaxRate { get; set; }
        public string? Query { get; set; }              
        public int TopK { get; set; } = 10;
    }
}
