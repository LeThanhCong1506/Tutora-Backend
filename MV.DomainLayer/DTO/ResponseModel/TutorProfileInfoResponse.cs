namespace MV.DomainLayer.DTO.ResponseModel
{
    /// <summary>
    /// Public tutor profile payload without schedule/package data.
    /// </summary>
    public class TutorProfileInfoResponse
    {
        public string? VideoIntroUrl { get; set; }

        public string? AvatarUrl { get; set; }
        public string? FullName { get; set; }
        public string? Headline { get; set; }
        public string? TeachingAreaCity { get; set; }
        public string? TeachingAreaDistrict { get; set; }
        public string? TeachingMode { get; set; }
        public List<SubjectInfo>? Subjects { get; set; }
        public List<TutorSubjectGradePriceResponse>? SubjectGradePrices { get; set; }

        public string? Bio { get; set; }
        public string? Degree { get; set; }
        public string? Education { get; set; }
        public double? Gpa { get; set; }
        public double? GpaScale { get; set; }
        public string? Experience { get; set; }

        public List<CertificateResponse>? Certificates { get; set; }

        public int TotalFeedbacks { get; set; }
        public double AverageRating { get; set; }
    }
}
