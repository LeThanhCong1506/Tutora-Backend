namespace MV.DomainLayer.DTO.ResponseModel
{
    public class TutorRecommendResponse
    {
        public List<TutorRecommendItem> Tutors { get; set; } = new();
        public int Total { get; set; }
        public bool AiRanked { get; set; }  
    }

    public class TutorRecommendItem
    {
        public string TutorId { get; set; } = "";
        public string FullName { get; set; } = "";
        public string? AvatarUrl { get; set; }
        public string? Headline { get; set; }
        public string? TeachingMode { get; set; }
        public string? TeachingAreaCity { get; set; }
        public string? TeachingAreaDistrict { get; set; }
        public double AverageRating { get; set; }
        public int TotalReviews { get; set; }
        public int TotalCompletedLessons { get; set; }
        public int TotalStudentsTaught { get; set; }
        public decimal? PricePerHour { get; set; }      
        public List<string> Subjects { get; set; } = new();
        public float? AiSimilarity { get; set; }        
        public string? ProfileUrl { get; set; }          
    }
}
